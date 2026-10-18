using Xunit;

// 多个测试类都会改 App.Config / DataStore 这类全局静态状态。
// 默认并行执行会让它们互相污染（表现为「单跑绿、全跑红」），这里统一串行。
// 当前 100 多个用例总耗时仍在秒级，串行的代价可以忽略。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
