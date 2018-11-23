using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ZooKeeperNet;

namespace ConsoleTest
{
    using System.Threading;

    using ZooKeeperNet;

    /// <summary>
    /// 使用前先Nuget搜索ZooKeeper.Net安装
    /// 安装ZooKeeper，百度自行搜索“Windows下安装ZooKeeper”，一大堆安装文档 
    /// 下载路径：https://mirrors.cnnic.cn/apache/zookeeper/
    /// </summary>
    public class ZooKeeperTest
    {
        public static bool Connect = false;
        public static void Test()
        {

            for (int i = 0; i < 10; i++)
            {
                Task.Factory.StartNew(
                    () =>
                    {
                        var lockObj = new ZooKeeperLock("127.0.0.1:2181", "testlock");
                        bool outPut = false;
                        while (true)
                        {
                            if (lockObj.GetLock())
                            {
                                Console.WriteLine(lockObj.GetCurrentId() + "获得锁正在执行操作");
                                Thread.Sleep(5 * 1000);
                                Console.WriteLine(lockObj.GetCurrentId() + "执行操作完成，即将释放锁");
                                lockObj.UnLock();
                                lockObj.Dispose();
                                break;
                            }
                            else
                            {
                                if (!outPut)
                                {
                                    Console.WriteLine(lockObj.GetCurrentId() + "在等待锁");
                                    outPut = true;
                                }

                            }
                        }
                    });

            }

        }

        private static void Fun()
        {
            Connect = false;
            //创建一个Zookeeper实例，第一个参数为目标服务器地址和端口，第二个参数为Session超时时间，第三个为节点变化时的回调方法 
            using (ZooKeeper zk = new ZooKeeper("127.0.0.1:2181", new TimeSpan(0, 0, 0, 10), new Watcher()))
            {
                while (!Connect)
                {

                }

                //var timeout = zk.SessionTimeout;
                var stat = zk.Exists("/root", true);
                if (stat == null)
                {
                    zk.Create("/root", null, Ids.OPEN_ACL_UNSAFE, CreateMode.Persistent);
                }
                var watches = zk.DataWatches;
                ////创建一个节点root，数据是mydata,不进行ACL权限控制，节点为永久性的(即客户端shutdown了也不会消失) 
                //zk.Create("/root", "mydata".GetBytes(), Ids.OPEN_ACL_UNSAFE, CreateMode.Persistent);

                //在root下面创建一个childone znode,数据为childone,不进行ACL权限控制，节点为永久性的 
                if (zk.Exists("/root/childone", true) == null)
                {
                    var result = zk.Create("/root/childone", null, Ids.OPEN_ACL_UNSAFE, CreateMode.Persistent);
                }

                //取得/root节点下的子节点名称,返回List<String> 
                zk.GetChildren("/root", true);
                //取得/root/childone节点下的数据,返回byte[] 
                var data = zk.GetData("/root/childone", true, null);

                //修改节点/root/childone下的数据，第三个参数为版本，如果是-1，那会无视被修改的数据版本，直接改掉
                zk.SetData("/root/childone", "childonemodify".GetBytes(), -1);
                //删除/root/childone这个节点，第二个参数为版本，－1的话直接删除，无视版本 
                zk.Delete("/root/childone", -1);
            }
        }
    }

    /// <summary>
    /// 注意Zookeeper的监听只能使用一次，原则上不允许永久监听，但可以在监听方法完成后再进行监听
    /// </summary>
    public class Watcher : IWatcher
    {
        public void Process(WatchedEvent @event)
        {
            //if (@event.Type == EventType.NodeDataChanged)
            //{
            //    Console.WriteLine("NodeDataChanged:" + @event.Path);
            //}
            if (KeeperState.SyncConnected == @event.State)
            {
                Console.WriteLine("已和服务建立连接");
                ZooKeeperTest.Connect = true;
            }
            Console.WriteLine(@event.Type + ":" + @event.Path);
        }
    }

    /// <summary>
    /// 主体思路
    /// 1. 在locks节点下创建临时顺序节点node_n
    /// 2. 判断当前创建的节点是否为locks节点下所有子节点中最小的子节点
    /// 3. 是则获取锁，进行业务处理，否则将节点从小到大排序，监听当前节点上一个节点的删除事件
    /// 4. 事件触发后回到步骤2进行判断，直至拿到锁
    /// </summary>
    public class ZooKeeperLock : IWatcher, IDisposable
    {
        private const string LockRootName = "/locks";
        private const int SessionTimeout = 300000;
        private ZooKeeper zooKeeper;

        /// <summary>
        /// 锁节点名字
        /// </summary>
        private string lockName;

        /// <summary>
        /// 当前id
        /// </summary>
        private string currentId;

        /// <summary>
        /// 拿不到锁的时候等待的前一个id
        /// </summary>
        private string waitId;

        private bool connected = false;

        public ZooKeeperLock(string server, string lockName)
        {
            this.lockName = lockName;
            this.zooKeeper = new ZooKeeper(server, new TimeSpan(0, 0, 0, SessionTimeout), this);
            while (!this.connected)
            {
                //保证和zookeeper建立连接后再进行节点操作
            }
            var stat = this.zooKeeper.Exists(LockRootName, false);
            if (stat == null)
            {
                this.zooKeeper.Create(LockRootName, null, Ids.OPEN_ACL_UNSAFE, CreateMode.Persistent);
            }
        }

        /// <summary>
        /// 获取锁
        /// </summary>
        /// <returns></returns>
        public bool GetLock(bool create = true)
        {
            if (this.currentId == null && create)
            {
                this.currentId = this.zooKeeper.Create(LockRootName + "/" + this.lockName + "_", new byte[0], Ids.OPEN_ACL_UNSAFE, CreateMode.EphemeralSequential);
            }
            var childrens = this.zooKeeper.GetChildren(LockRootName, false);
            if (childrens == null || childrens.Count() == 1)
            {
                return true;
            }
            var orderChildrens = childrens.OrderBy(p => p).ToList();
            var index = orderChildrens.FindIndex(p => p.Equals(this.currentId.Replace(LockRootName + "/", "")));
            if (index == 0)
            {
                return true;
            }
            this.waitId = LockRootName + "/" + orderChildrens[index - 1];
            var stat = this.zooKeeper.Exists(this.waitId, this);
            if (stat == null)
            {
                this.GetLock(false);
            }
            return false;
        }

        /// <summary>
        /// 释放锁
        /// 删除节点
        /// </summary>
        public void UnLock()
        {
            if (this.currentId == null)
            {
                return;
            }
            this.zooKeeper.Delete(this.currentId, -1);
            this.currentId = null;
        }

        /// <summary>
        /// 获取当前的节点id
        /// </summary>
        /// <returns></returns>
        public string GetCurrentId()
        {
            return this.currentId;
        }

        /// <summary>
        /// 监听方法
        /// 1. 主要监听删除节点操作
        /// 2. 监听是否建立连接
        /// </summary>
        /// <param name="event"></param>
        public void Process(WatchedEvent @event)
        {
            if (KeeperState.SyncConnected == @event.State)
            {
                this.connected = true;
            }

            if (@event.Type == EventType.NodeDeleted)
            {
                this.GetLock(false);
            }
        }

        public void Dispose()
        {
            this.zooKeeper.Dispose();
        }
    }
}
