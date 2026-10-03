#!/usr/bin/env node
/**
 * 把 CaiBotWindy 生成的菜单 / 指令面板配置发布到 QQ 开放平台。
 *
 * 背景：`menu/menu.json` 与 `menu/panels.json` 的内容就是开放平台的**请求体**——
 *   - `menu.json`   = `PUT /v2/menu` 的 body（形如 `{ menu: { items: [...] } }`）
 *   - `panels.json` = 数组，每条即 `POST /v2/panels` 的 body
 * 所以可以直接推送，不需要经过 qq-bot-menu-panel 之类的可视化工具
 * （那个工具只支持「连平台 → 拉取 → 编辑 → 存回」，没有导入本地文件的功能）。
 *
 * 用法：
 *   node publish_menu.mjs probe           查询平台现状（只读，不改动任何配置）
 *   node publish_menu.mjs push-menu       推送自定义菜单（PUT /v2/menu，**覆盖式**）
 *   node publish_menu.mjs push-panels     推送指令面板（POST /v2/panels，逐条新建）
 *   node publish_menu.mjs push            依次执行上面两项
 *
 * 凭据通过环境变量传入：
 *   QQ_BOT_APPID=xxx QQ_BOT_SECRET=yyy node publish_menu.mjs probe
 */
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const API_BASE = 'https://api.bot.qq.com'
const HERE = path.dirname(fileURLToPath(import.meta.url))
const MENU_FILE = path.resolve(HERE, '..', 'menu', 'menu.json')
const PANELS_FILE = path.resolve(HERE, '..', 'menu', 'panels.json')

/** 平台限制，与 qq-bot-menu-panel 的 src/utils/constants.ts 保持一致 */
const LIMITS = { menuItems: 10, subMenuItems: 5, panelItems: 20, panelCount: 20 }

/** 官方错误码排查提示 */
const HINTS = {
  100001: '请求过于频繁，请稍后重试',
  100007: 'AppID 无效或机器人状态异常',
  100016: 'AppID 或 AppSecret 不正确',
  40030009: '指令面板操作进行中，请稍后重试',
  40030013: '超出数量限制，请减少配置数量',
  40030014: '菜单类型不合法（仅 switch / send_message / link / menu）',
  40030015: '面板元素类型不合法（仅 command / link）',
  40030020: '内容存在安全风险，请修改后重试',
}

const APP_ID = process.env.QQ_BOT_APPID
const APP_SECRET = process.env.QQ_BOT_SECRET

function fail(message) {
  console.error(`\n✗ ${message}\n`)
  process.exit(1)
}

function hintOf(errCode) {
  return HINTS[errCode] ? `（${HINTS[errCode]}）` : ''
}

function describe(result) {
  if (result.ok) return 'ok'
  const message = result.data?.message ?? `HTTP ${result.status}`
  const code = result.errCode ? `err_code=${result.errCode} ` : ''
  return `${code}${message}${hintOf(result.errCode)}`
}

async function getAccessToken() {
  let response
  try {
    response = await fetch(`${API_BASE}/app/getAppAccessToken`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ appId: APP_ID, clientSecret: APP_SECRET }),
    })
  } catch (cause) {
    fail(`无法连接开放平台：${cause.message}`)
  }

  const payload = await response.json().catch(() => null)
  if (!payload?.access_token) {
    const code = payload?.code ?? payload?.err_code
    fail(
      `获取 access_token 失败：${payload?.message ?? `HTTP ${response.status}`}` +
        (code ? `（err_code ${code}）${hintOf(code)}` : ''),
    )
  }

  console.log(`✓ access_token 获取成功（有效期 ${payload.expires_in}s）`)
  return String(payload.access_token)
}

async function callApi(token, method, apiPath, body) {
  let response
  try {
    response = await fetch(`${API_BASE}${apiPath}`, {
      method,
      headers: {
        Authorization: `QQBot ${token}`,
        'Content-Type': 'application/json; charset=utf-8',
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch (cause) {
    return { ok: false, status: 502, errCode: undefined, data: { message: cause.message } }
  }

  const text = await response.text()
  let data
  try {
    data = text ? JSON.parse(text) : null
  } catch {
    data = { message: text }
  }

  // 官方约定：以 err_code 判定成败，不要依赖 message
  const errCode = data?.err_code ?? data?.code ?? 0
  return {
    ok: response.ok && !errCode,
    status: response.status,
    errCode: errCode || undefined,
    data,
    traceId: response.headers.get('x-tps-trace-id'),
  }
}

/** 自动翻页取全部指令面板（GET /v2/panels 是分页接口，只取一页会漏） */
async function listAllPanels(token, scope) {
  const records = []
  let cursor = ''
  for (let page = 0; page < 30; page++) {
    const suffix = cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''
    const result = await callApi(token, 'GET', `/v2/panels?scope=${scope}${suffix}`)
    if (!result.ok) return { ok: false, records, error: result }
    records.push(...(result.data?.records ?? []))
    const next = result.data?.next_cursor ?? ''
    if (!next || result.data?.is_end) break
    cursor = next
  }
  return { ok: true, records }
}

async function probe(token) {
  console.log('\n=== GET /v2/menu（自定义菜单，仅单聊生效）===')
  const menu = await callApi(token, 'GET', '/v2/menu')
  if (menu.ok) {
    const items = menu.data?.menu?.items ?? []
    console.log(`当前版本 v${menu.data?.version ?? '?'}，${items.length} 个一级菜单：`)
    for (const item of items) {
      const subs = item.sub_menu_items?.length ?? 0
      console.log(`  - [${item.type}] ${item.name}${subs ? `（${subs} 个子项）` : ''}`)
    }
    if (!items.length) console.log('  （平台侧尚无自定义菜单）')
  } else {
    console.log(`  ${describe(menu)}`)
  }

  for (const scope of ['group', 'c2c', 'channel', 'dm']) {
    const { ok, records, error } = await listAllPanels(token, scope)
    if (!ok) {
      console.log(`\n=== GET /v2/panels?scope=${scope} ===\n  ${describe(error)}`)
      continue
    }
    console.log(`\n=== GET /v2/panels?scope=${scope} → 共 ${records.length} 个面板 ===`)
    for (const record of records) {
      console.log(
        `  - ${record.panel_id}  「${record.panel?.remark ?? ''}」` +
          ` ${record.panel?.items?.length ?? 0} 元素  v${record.version ?? '?'}`,
      )
    }
  }
}

async function pushMenu(token) {
  const body = JSON.parse(fs.readFileSync(MENU_FILE, 'utf8'))
  const items = body?.menu?.items ?? []
  console.log(`\n=== PUT /v2/menu（${items.length} 个一级菜单，覆盖式）===`)

  if (!items.length) return fail('menu.json 里没有 menu.items')
  if (items.length > LIMITS.menuItems) {
    return fail(`一级菜单 ${items.length} 个，超出平台上限 ${LIMITS.menuItems}`)
  }

  const result = await callApi(token, 'PUT', '/v2/menu', body)
  console.log(result.ok ? `✓ 自定义菜单已保存，版本 v${result.data?.version ?? '?'}` : `✗ 失败：${describe(result)}`)
  return result.ok
}

async function pushPanels(token) {
  const panels = JSON.parse(fs.readFileSync(PANELS_FILE, 'utf8'))
  console.log(`\n=== POST /v2/panels（共 ${panels.length} 个面板）===`)

  if (panels.length > LIMITS.panelCount) {
    return fail(`面板 ${panels.length} 个，超出平台上限 ${LIMITS.panelCount}`)
  }

  // 平台限制：同一 scope + target 只能存在 1 个面板。
  // 因此同步策略是「先清空该场景 → 再逐个重建」，结果与配置文件完全一致，且可重复运行。
  for (const scope of [...new Set(panels.map((entry) => entry.scope))]) {
    const { ok, records } = await listAllPanels(token, scope)
    if (!ok) continue

    for (const record of records) {
      const removed = await callApi(token, 'DELETE', `/v2/panels/${record.panel_id}`)
      await new Promise((resolve) => setTimeout(resolve, 1200))
      console.log(
        `  - ${String(scope).padEnd(8)}清空旧面板「${record.panel?.remark}」` +
          (removed.ok ? '✓' : `✗ ${describe(removed)}`),
      )
    }
  }

  let success = 0
  for (const entry of panels) {
    const label = `${String(entry.scope).padEnd(8)}${String(entry.panel?.remark ?? '').padEnd(26)}`

    const payload = {
      scope: entry.scope,
      target_type: entry.target_type,
      panel: {
        remark: entry.panel?.remark,
        items: entry.panel?.items ?? [],
      },
    }

    const result = await callApi(token, 'POST', '/v2/panels', payload)
    if (!result.ok) {
      console.log(`  ✗ ${label} → ${describe(result)}`)
      continue
    }

    const panelId = result.data?.panel_id ?? '?'
    // 平台的指令面板操作是异步串行的：连发会互相覆盖，建完必须回查确认真的落库
    await new Promise((resolve) => setTimeout(resolve, 2500))
    const { records } = await listAllPanels(token, entry.scope)
    const alive = records.some((record) => record.panel_id === panelId)

    if (alive) {
      success++
      console.log(`  ✓ ${label} → ${panelId}（该场景现共 ${records.length} 个）`)
    } else {
      console.log(`  ✗ ${label} → ${panelId} 创建后未落库（该场景现共 ${records.length} 个）`)
    }
  }

  console.log(`\n成功 ${success} / ${panels.length}`)
  return success === panels.length
}

async function main() {
  const command = process.argv[2] ?? 'probe'
  if (!['probe', 'raw', 'push-menu', 'push-panels', 'push'].includes(command)) {
    return fail(`未知命令「${command}」，可用：probe | raw | push-menu | push-panels | push`)
  }
  if (!APP_ID || !APP_SECRET) {
    return fail('缺少凭据，请设置环境变量 QQ_BOT_APPID 与 QQ_BOT_SECRET')
  }

  console.log(`AppID: ${APP_ID}`)
  const token = await getAccessToken()

  if (command === 'probe') return probe(token)
  if (command === 'raw') {
    // 注意：路径不要带前导斜杠，否则会被 Git Bash 的 MSYS 路径转换吃掉
    const raw = process.argv[3]
    if (!raw) return fail('用法：raw "v2/panels?scope=group"，输出原始响应便于排查')
    const apiPath = raw.startsWith('/') ? raw : `/${raw}`
    const result = await callApi(token, 'GET', apiPath)
    console.log(JSON.stringify(result, null, 2))
    return
  }
  if (command === 'push-menu') {
    await pushMenu(token)
    return
  }
  if (command === 'push-panels') {
    await pushPanels(token)
    return
  }

  const menuOk = await pushMenu(token)
  const panelsOk = menuOk ? await pushPanels(token) : false
  console.log(`\n总结果：菜单 ${menuOk ? '✓' : '✗'}　面板 ${panelsOk ? '✓' : '✗'}`)
}

main().catch((error) => fail(error?.stack ?? String(error)))
