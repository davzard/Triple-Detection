using System;
using Prism.DryIoc;

namespace TripleDetection.Presentation
{
    /// <summary>
    /// 视图代码后置中解析 ViewModel 的辅助类（Prism 9 的 IContainerProvider 只提供非泛型 Resolve）
    /// </summary>
    public static class ViewResolver
    {
        public static T Resolve<T>() where T : class
        {
            var app = System.Windows.Application.Current as PrismApplication
                ?? throw new InvalidOperationException("Application 尚未初始化");
            return (T)app.Container.Resolve(typeof(T));
        }

        public static T Resolve<T>(Type parameterType, object parameter) where T : class
        {
            var app = System.Windows.Application.Current as PrismApplication
                ?? throw new InvalidOperationException("Application 尚未初始化");
            return (T)app.Container.Resolve(typeof(T), (parameterType, parameter));
        }

        /// <summary>尝试解析；容器不可用（设计器）或解析失败时返回 null。</summary>
        public static T? TryResolve<T>() where T : class
        {
            try
            {
                var app = System.Windows.Application.Current as PrismApplication;
                if (app == null) return null;
                return (T)app.Container.Resolve(typeof(T));
            }
            catch
            {
                return null;
            }
        }
    }
}
