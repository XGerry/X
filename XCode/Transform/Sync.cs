﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using NewLife.Log;
using XCode.Membership;

namespace XCode.Transform
{
    /// <summary>数据同步，不同实体类之间</summary>
    /// <typeparam name="TSource">源实体类</typeparam>
    /// <typeparam name="TTarget">目标实体类</typeparam>
    public class Sync<TSource, TTarget> : Sync
        where TSource : Entity<TSource>, new()
        where TTarget : Entity<TTarget>, new()
    {
        #region 构造
        /// <summary>实例化数据抽取器</summary>
        public Sync() : base(Entity<TSource>.Meta.Factory, Entity<TTarget>.Meta.Factory) { }
        #endregion

        /// <summary>启动时检查参数</summary>
        public override void Start()
        {
            // 如果目标表为空，则使用仅插入
            if (!InsertOnly)
            {
                if (Target.Count == 0) InsertOnly = true;
            }

            base.Start();
        }

        /// <summary>处理单行数据</summary>
        /// <param name="source">源实体</param>
        protected override IEntity SyncItem(IEntity source)
        {
            var target = GetItem(source, out var isNew);

            SyncItem(source as TSource, target as TTarget, isNew);

            SaveItem(target, isNew);

            return target;
        }

        /// <summary>处理单行数据</summary>
        /// <param name="source">源实体</param>
        /// <param name="target">目标实体</param>
        /// <param name="isNew">是否新增</param>
        protected virtual IEntity SyncItem(TSource source, TTarget target, Boolean isNew)
        {
            // 同名字段对拷
            target?.CopyFrom(source, true);

            return target;
        }
    }

    /// <summary>数据同步，相同实体类不同表和库</summary>
    /// <typeparam name="TSource">源实体类</typeparam>
    public class Sync<TSource> : Sync
        where TSource : Entity<TSource>, new()
    {
        #region 属性
        /// <summary>目标连接</summary>
        public String TargetConn { get; set; }

        /// <summary>目标表名</summary>
        public String TargetTable { get; set; }
        #endregion

        #region 构造
        /// <summary>实例化数据抽取器</summary>
        public Sync() : base(Entity<TSource>.Meta.Factory, Entity<TSource>.Meta.Factory) { }
        #endregion

        /// <summary>启动时检查参数</summary>
        public override void Start()
        {
            if (TargetConn.IsNullOrEmpty()) throw new ArgumentNullException(nameof(TargetConn));
            if (TargetTable.IsNullOrEmpty()) throw new ArgumentNullException(nameof(TargetTable));

            // 如果目标表为空，则使用仅插入
            if (!InsertOnly)
            {
                var count = Target.Split(TargetConn, TargetTable, () => Target.Count);
                if (count == 0) InsertOnly = true;
            }

            base.Start();
        }

        /// <summary>同步数据列表时，在目标表上执行</summary>
        /// <param name="list"></param>
        /// <param name="set"></param>
        /// <returns></returns>
        protected override Int32 OnSync(IList<IEntity> list, IExtractSetting set)
        {
            return Target.Split(TargetConn, TargetTable, () => base.OnSync(list, set));
        }

        /// <summary>处理单行数据</summary>
        /// <param name="source">源实体</param>
        protected override IEntity SyncItem(IEntity source)
        {
            var isNew = InsertOnly;
            var target = InsertOnly ? source : GetItem(source, out isNew);

            SyncItem(source as TSource, target as TSource, isNew);

            SaveItem(target, isNew);

            return target;
        }

        /// <summary>处理单行数据</summary>
        /// <param name="source">源实体</param>
        /// <param name="target">目标实体</param>
        /// <param name="isNew">是否新增</param>
        protected virtual IEntity SyncItem(TSource source, TSource target, Boolean isNew)
        {
            // 同名字段对拷
            target?.CopyFrom(source, true);

            return target;
        }
    }

    /// <summary>数据同步</summary>
    public class Sync : ETL
    {
        #region 属性
        /// <summary>目标实体工厂。分批统计时不需要设定</summary>
        public IEntityOperate Target { get; set; }

        /// <summary>仅插入，不用判断目标是否已有数据</summary>
        public Boolean InsertOnly { get; set; }
        #endregion

        #region 构造
        /// <summary>实例化数据抽取器</summary>
        public Sync() : base() { }

        /// <summary>实例化数据抽取器</summary>
        /// <param name="source"></param>
        public Sync(IEntityOperate source, IEntityOperate target) : base(source) { Target = target; }
        #endregion

        #region 数据处理
        /// <summary>处理列表，传递批次配置，支持多线程和异步</summary>
        /// <remarks>
        /// 子类可以根据需要重载该方法，实现异步处理。
        /// 异步处理之前，需要先保存配置
        /// </remarks>
        /// <param name="list">实体列表</param>
        /// <param name="set">本批次配置</param>
        /// <param name="fetchCost">抽取数据耗时</param>
        protected override void ProcessList(IList<IEntity> list, IExtractSetting set, Double fetchCost)
        {
            var sw = Stopwatch.StartNew();

            var count = OnSync(list, set);

            sw.Stop();

            OnFinished(list, set, count, fetchCost, sw.Elapsed.TotalMilliseconds);
        }
        #endregion

        #region 数据同步
        /// <summary>处理列表，传递批次配置，支持多线程</summary>
        /// <param name="list">实体列表</param>
        /// <param name="set">本批次配置</param>
        protected virtual Int32 OnSync(IList<IEntity> list, IExtractSetting set)
        {
            var count = 0;

            // 批量事务提交
            var fact = Target;
            if (fact == null) throw new ArgumentNullException(nameof(Target));

            fact.BeginTransaction();
            try
            {
                foreach (var source in list)
                {
                    try
                    {
                        SyncItem(source);

                        count++;
                    }
                    catch (Exception ex)
                    {
                        ex = OnError(source, set, ex);
                        if (ex != null) throw ex;
                    }
                }
                fact.Commit();
            }
            catch
            {
                fact.Rollback();
                throw;
            }

            return count;
        }

        /// <summary>同步单行数据</summary>
        /// <param name="source">源实体</param>
        /// <returns></returns>
        protected virtual IEntity SyncItem(IEntity source)
        {
            var isNew = InsertOnly;
            var target = InsertOnly ? source : GetItem(source, out isNew);

            // 同名字段对拷
            target?.CopyFrom(source, true);

            SaveItem(target, isNew);

            return target;
        }

        /// <summary>根据源实体获取目标实体</summary>
        /// <param name="source">源实体</param>
        /// <param name="isNew">是否新增</param>
        /// <returns></returns>
        protected virtual IEntity GetItem(IEntity source, out Boolean isNew)
        {
            var key = source[Extracter.Factory.Unique.Name];

            // 查找目标，如果不存在则创建
            isNew = false;
            var fact = Target;
            var target = fact.FindByKey(key);
            if (target == null)
            {
                target = fact.Create();
                target[fact.Unique.Name] = key;
                isNew = true;
            }

            return target;
        }

        /// <summary>保存目标实体</summary>
        /// <param name="target"></param>
        /// <param name="isNew"></param>
        protected virtual void SaveItem(IEntity target, Boolean isNew)
        {
            var st = Stat;
            if (isNew)
                target.Insert();
            else
            {
                target.Update();
                st.Changes++;
            }
        }
        #endregion
    }
}