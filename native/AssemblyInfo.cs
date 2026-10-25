using System.Runtime.CompilerServices;

// 离线测试可以注入传输层，正式入口始终使用客户端自己的直连配置。
[assembly: InternalsVisibleTo("CMCLauncher.Tests")]
