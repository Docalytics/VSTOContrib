﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Office.Core;
using VSTOContrib.Core.Domain;
using VSTOContrib.Core.RibbonFactory.Interfaces;
using VSTOContrib.Core.RibbonFactory.Internal;

namespace VSTOContrib.Core.RibbonFactory
{
    /// <summary>
    ///     Because you cannot make a generic type COM visible, moving all code that requires generics into this class
    /// </summary>
    class RibbonFactoryController : IRibbonFactoryController
    {
        readonly ViewModelResolver ribbonViewModelResolver;
        readonly VstoContribContext vstoContribContext;
        readonly CustomTaskPaneRegister customTaskPaneRegister;
        readonly OfficeApplicationDomain domain;

        public RibbonFactoryController(
            IViewContextProvider viewContextProvider,
            VstoContribContext vstoContribContext,
            IOfficeApplicationEvents officeApplicationEvents)
        {
            this.vstoContribContext = vstoContribContext;
            var ribbonTypes = GetTRibbonTypesInAssemblies(vstoContribContext.Assemblies).ToList();
            domain = new OfficeApplicationDomain(officeApplicationEvents);

            customTaskPaneRegister = new CustomTaskPaneRegister(vstoContribContext.AddinBase, domain);
            ribbonViewModelResolver = new ViewModelResolver(
                ribbonTypes, viewContextProvider, 
                vstoContribContext, officeApplicationEvents);

            var ribbonXmlRewriter = new RibbonXmlRewriter(vstoContribContext, ribbonViewModelResolver);

            var loadExpression = ((Expression<Action<RibbonFactory>>)(r => r.Ribbon_Load(null)));
            string loadMethodName = loadExpression.GetMethodName();

            foreach (Type viewModelType in ribbonTypes)
            {
                ribbonXmlRewriter.LocateAndRegisterViewXml(viewModelType, loadMethodName, vstoContribContext.FallbackRibbonType);
            }
        }

        public string GetCustomUI(string ribbonId)
        {
            return !vstoContribContext.RibbonXmlFromTypeLookup.ContainsKey(ribbonId)
                       ? null
                       : vstoContribContext.RibbonXmlFromTypeLookup[ribbonId];
        }

        public object InvokeGet(IRibbonControl control, Expression<Action> caller, params object[] parameters)
        {
            var methodName = caller.GetMethodName();
            CallbackTarget callbackTarget = vstoContribContext.TagToCallbackTargetLookup[control.Tag + methodName];

            var officeContext = (object)control.Context;
            var context = domain.GetContext(officeContext);
            VstoContribLog.Debug(l => l("Ribbon get value callback {0} being invoked on {1} (View: {2}, ViewModel: {3})",
                methodName, control.Id, context.ActiveView.Window.Window.ToLogFormat(), context.ViewModel.ToLogFormat()));

            Type type = context.ViewModel.GetType();
            PropertyInfo property = type.GetProperty(callbackTarget.Method);

            if (property != null)
            {
                //TODO Catch/wrap exception properly
                return type.InvokeMember(callbackTarget.Method,
                                         BindingFlags.GetProperty,
                                         null,
                                         context.ViewModel,
                                         null);
            }

            try
            {
                return type.InvokeMember(callbackTarget.Method,
                                         BindingFlags.InvokeMethod,
                                         null,
                                         context.ViewModel,
                                         new[]
                                         {
                                             control
                                         }
                                             .Concat(parameters)
                                             .ToArray());
            }
            catch (MissingMethodException)
            {
                throw new InvalidOperationException(
                    string.Format("Expecting method with signature: {0}.{1}(IRibbonControl control)",
                                  type.Name,
                                  callbackTarget.Method));
            }
        }

        public void Invoke(IRibbonControl control, Expression<Action> caller, params object[] parameters)
        {
            try
            {
                var methodName = caller.GetMethodName();
                CallbackTarget callbackTarget =
                    vstoContribContext.TagToCallbackTargetLookup[control.Tag + methodName];

                var officeContext = (object)control.Context;
                var context = domain.GetContext(officeContext);
                VstoContribLog.Debug(l => l("Ribbon get value callback {0} being invoked on {1} (View: {2}, ViewModel: {3})",
                    methodName, control.Id, context.ActiveView.Window.Window.ToLogFormat(), context.ViewModel.ToLogFormat()));

                Type type = context.ViewModel.GetType();
                PropertyInfo property = type.GetProperty(callbackTarget.Method);

                if (property != null)
                {
                    type.InvokeMember(callbackTarget.Method,
                        BindingFlags.SetProperty,
                        null,
                        context.ViewModel,
                        new[]
                        {
                            parameters.Single()
                        });
                }
                else
                {
                    type.InvokeMember(callbackTarget.Method,
                        BindingFlags.InvokeMethod,
                        null,
                        context.ViewModel,
                        new[]
                        {
                            control
                        }
                            .Concat(parameters)
                            .ToArray());
                }
            }
            catch (TargetInvocationException e)
            {
                var innerEx = e.InnerException;
                PreserveStackTrace(innerEx);
                if (vstoContribContext.ErrorHandlers.Count == 0)
                {
                    Trace.TraceError(innerEx.ToString());
                }

                var handled = vstoContribContext.ErrorHandlers.Any(errorHandler => errorHandler.Handle(innerEx));

                if (!handled)
                    throw innerEx;
            }
        }

        // http://weblogs.asp.net/fmarguerie/archive/2008/01/02/rethrowing-exceptions-and-preserving-the-full-call-stack-trace.aspx
        internal static void PreserveStackTrace(Exception exception)
        {
            MethodInfo preserveStackTrace = typeof(Exception).GetMethod("InternalPreserveStackTrace",
              BindingFlags.Instance | BindingFlags.NonPublic);
            preserveStackTrace.Invoke(exception, null);
        }

        public void RibbonLoaded(IRibbonUI ribbonUi)
        {
            domain.RibbonLoaded(ribbonUi);
        }

        static IEnumerable<Type> GetTRibbonTypesInAssemblies(IEnumerable<Assembly> assemblies)
        {
            VstoContribLog.Debug(_ => _("Discovering ViewModels"));

            Type ribbonViewModelType = typeof(IRibbonViewModel);
            return assemblies
                .Select(assembly =>
                    {
                        VstoContribLog.Debug(_ => _("Discovering ViewModels in {0}", assembly.GetName().Name));
                        var types = assembly.GetTypes();
                        var viewModelTypes = types.Where(ribbonViewModelType.IsAssignableFrom).ToArray();
                        VstoContribLog.Debug(_ => _("Found:{0}", string.Join(string.Empty, viewModelTypes.Select(vm => "\r\n  " + vm.Name))));
                        return viewModelTypes;
                    }
                )
                .SelectMany(vm => vm);
        }

        public void Dispose()
        {
            ribbonViewModelResolver.Dispose();
            customTaskPaneRegister.Dispose();
        }
    }
}