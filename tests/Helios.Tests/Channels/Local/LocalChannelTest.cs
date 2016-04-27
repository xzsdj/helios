﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Helios.Channels;
using Helios.Channels.Local;
using Helios.Channels.Bootstrap;
using Helios.Logging;
using Xunit;

namespace Helios.Tests.Channels.Local
{
    public class LocalChannelTest : IDisposable
    {
        private static readonly ILogger Logger = LoggingFactory.GetLogger<LocalChannelTest>();
        private static readonly LocalAddress TEST_ADDRESS = new LocalAddress("test.id");

        private IEventLoopGroup _group1;
        private IEventLoopGroup _group2;
        private IEventLoopGroup _sharedGroup;

        public LocalChannelTest()
        {
            _group1 = new MultithreadEventLoopGroup(2);
            _group2 = new MultithreadEventLoopGroup(2);
            _sharedGroup = new MultithreadEventLoopGroup(1);
        }

        [Fact]
        public void LocalChannel_should_reuse_LocalAddress()
        {
            for (var i = 0; i < 2; i++)
            {
                var cb = new ClientBootstrap();
                var sb = new ServerBootstrap();

                cb.Group(_group1).Channel<LocalChannel>().Handler(new TestHandler());

                sb.Group(_group2).Channel<LocalServerChannel>().ChildHandler(new ActionChannelInitializer<LocalChannel>(
                    channel =>
                    {
                        channel.Pipeline.AddLast(new TestHandler());
                    }));

                IChannel sc = null;
                IChannel cc = null;

                try
                {
                    // Start server
                    sc = sb.BindAsync(TEST_ADDRESS).Result;
                    var latch = new CountdownEvent(1);

                    // Connect to the server
                    cc = cb.ConnectAsync(sc.LocalAddress).Result;
                    var cCpy = cc;
                    cc.EventLoop.Execute(o =>
                    {
                        var c = (LocalChannel) o;
                        c.Pipeline.FireChannelRead("Hello, World");
                        latch.Signal();
                    }, cCpy);

                    latch.Wait(TimeSpan.FromSeconds(5));
                    Assert.True(latch.IsSet);

                    CloseChannel(cc);
                    CloseChannel(sc);
                    sc.CloseCompletion.Wait();

                    Assert.Null(LocalChannelRegistry.Get(TEST_ADDRESS));
                }
                finally
                {
                    CloseChannel(cc);
                    CloseChannel(sc);
                }
            }
        }

        private static void CloseChannel(IChannel cc)
        {
            cc?.CloseAsync().Wait();
        }

        private class TestHandler : ChannelHandlerAdapter
        {
            public override void ChannelRead(IChannelHandlerContext context, object message)
            {
                Logger.Info("Received message: {0}", message);
                // todo: reference counting
            }
        }

        public void Dispose()
        {
            var t1 = _group1.ShutdownGracefullyAsync();
            var t2 = _group2.ShutdownGracefullyAsync();
            var t3 = _sharedGroup.ShutdownGracefullyAsync();
            Task.WaitAll(t1, t2, t3);
        }
    }
}
