﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.Office.Core;
using VSTOContrib.Core.Annotations;
using VSTOContrib.Core.RibbonFactory.Interfaces;

namespace VSTOContrib.Core.RibbonFactory.Internal
{
    internal class ViewModelResolver : IViewModelResolver
    {
        /// <summary>
        /// Used when a new explorer or inspector is created to lookup the appropriate viewmodel type
        /// </summary>
        readonly Dictionary<string, Type> ribbonTypeLookup;
        /// <summary>
        /// Internal lookup for Context instances to view model lookups
        /// </summary>
        readonly Dictionary<object, IRibbonViewModel> contextToViewModelLookup;
        /// <summary>
        /// Looks up ViewModelType, callback method name, control id, controlId used to invalidate :)
        /// </summary>
        readonly Dictionary<Type, List<KeyValuePair<string, string>>> notifyChangeTargetLookup;

        readonly IViewContextProvider viewContextProvider;
        readonly VstoContribContext vstoContribContext;
        readonly IOfficeApplicationEvents officeApplicationEvents;

        public ViewModelResolver(
            IEnumerable<Type> viewModelType,
            IViewContextProvider viewContextProvider, 
            VstoContribContext context,
            IOfficeApplicationEvents officeApplicationEvents)
        {
            notifyChangeTargetLookup = new Dictionary<Type, List<KeyValuePair<string, string>>>();
            ribbonTypeLookup = new Dictionary<string, Type>();
            contextToViewModelLookup = new Dictionary<object, IRibbonViewModel>();
            this.viewContextProvider = viewContextProvider;
            vstoContribContext = context;

            this.officeApplicationEvents = officeApplicationEvents;
            //officeApplicationEvents.NewView += ViewProviderNewView;
            //officeApplicationEvents.ViewClosed += ViewProviderViewClosed;

            foreach (var ribbonType in viewModelType)
            {
                CreateRibbonTypeToViewModelTypeLookup(ribbonType, context.FallbackRibbonType);
            }
        }

        void InvalidateRibbonForViewModel(IRibbonViewModel viewModel)
        {
            if (viewModel.RibbonUi == null) return;
            foreach (var targets in notifyChangeTargetLookup[viewModel.GetType()])
            {
                viewModel.RibbonUi.InvalidateControl(targets.Value);
            }
        }

        //void ViewProviderNewView(object sender, NewViewEventArgs e)
        //{
        //    VstoContribLog.Debug(_ => _("ViewProvider.NewView Raised, Type: {0}, View: {1}, Context: {2}",
        //        e.RibbonType, e.ViewInstance.ToLogFormat(), e.ViewContext.ToLogFormat()));
        //    if (ribbonUiLookup.ContainsKey("default"))
        //    {
        //        ribbonUiLookup.Add(e.RibbonType, ribbonUiLookup["default"]);
        //        ribbonUiLookup.Remove("default");
        //    }
        //    var viewModel = GetOrCreateViewModel(e.RibbonType, e.ViewContext ?? NullContext.Instance, e.ViewInstance);
        //    InvalidateRibbonForViewModel(viewModel);
        //    if (viewModel == null) return;
        //    e.Handled = true;
        //}

        //void ViewProviderViewClosed(object sender, ViewClosedEventArgs e)
        //{
        //    VstoContribLog.Debug(_ => _("ViewProvider.ViewClosed Raised, View: {0}, Context: {1}",
        //           e.View.ToLogFormat(), e.Context.ToLogFormat()));

        //    CleanupViewModel(e.Context);
        //    officeApplicationEvents.CleanupReferencesTo(e.View, e.Context);
        //}

        //IRibbonViewModel GetOrCreateViewModel(string ribbonType, object viewContext, OfficeWin32Window viewInstance)
        //{
        //    if (!ribbonTypeLookup.ContainsKey(ribbonType)) return null;
        //    if (contextToViewModelLookup.ContainsKey(viewContext))
        //    {
        //        //Tell viewmodel there is a new view active
        //        var ribbonViewModel = contextToViewModelLookup[viewContext];
        //        VstoContribLog.Debug(_ => _("ViewModel {0} found for context {1}", ribbonViewModel.ToLogFormat(), viewContext.ToLogFormat()));
        //        ribbonViewModel.CurrentView = viewInstance.Window;
        //        return ribbonViewModel;
        //    }

        //    currentlyLoadingRibbon = ribbonType;
        //    IRibbonViewModel buildViewModel = BuildViewModel(ribbonType, viewInstance, viewContext);
        //    contextToViewModelLookup.Add(viewContext, buildViewModel);
        //    return buildViewModel;
        //}

        private void CreateRibbonTypeToViewModelTypeLookup(Type ribbonViewModel, [CanBeNull] string ribbonFallbackType)
        {
            foreach (var value in ViewModelRibbonTypesLookupProvider.Instance.GetRibbonTypesFor(ribbonViewModel, ribbonFallbackType))
            {
                if (ribbonTypeLookup.ContainsKey(value))
                    throw new InvalidOperationException("You cannot have two view models which are registered for the same ribbon type");
                ribbonTypeLookup.Add(value, ribbonViewModel);
            }
        }

        //public IRibbonViewModel ResolveInstanceFor(OfficeWin32Window view)
        //{

        //    var context = viewContextProvider.GetContextForView(view) ?? NullContext.Instance;

        //    //Sometimes can happen that view provider has not got events to tell us about a new view
        //    // so we will have to try and create it
        //    if (!contextToViewModelLookup.ContainsKey(context))
        //    {
        //        var ribbonTypeForView = viewContextProvider.GetRibbonTypeForView(view);

        //        GetOrCreateViewModel(ribbonTypeForView, context, view);
        //    }

        //    return contextToViewModelLookup[context];
        //}

        //public void RibbonLoaded(IRibbonUI ribbonUi)
        //{
        //    foreach (var viewModelLookup in contextToViewModelLookup.Values
        //        .Where(viewModel => viewModel.GetType() == viewModelType && viewModel.RibbonUi == null))
        //    {
        //        VstoContribLog.Debug(_ => _("Setting RibbonUi [{0}] for ViewModel", ribbonUi.ToLogFormat()));
        //        viewModelLookup.RibbonUi = ribbonUi;
        //        InvalidateRibbonForViewModel(viewModelLookup);
        //    }
        //}

        public IRibbonViewModel BuildViewModel(string ribbonType, IRibbonUI ribbonUi, object viewContext)
        {
            var viewModelType = ribbonTypeLookup[ribbonType];
            VstoContribLog.Info(_ => _("Building ViewModel of type {1} for ribbon {1} with context {2}", 
                viewModelType.Name, ribbonType, viewContext.ToLogFormat()));
            var ribbonViewModel = vstoContribContext.ViewModelFactory.Resolve(viewModelType);
            ribbonViewModel.VstoFactory = vstoContribContext.VstoFactory;



            VstoContribLog.Debug(_ => _("Setting RibbonUi [{0}] for ViewModel", ribbonUi.ToLogFormat()));
            ribbonViewModel.RibbonUi = ribbonUi;
            //ribbonViewModel.CurrentView = viewInstance.Window;
            ListenForINotifyPropertyChanged(ribbonViewModel);
            ribbonViewModel.Initialised(viewContext);

            return ribbonViewModel;
        }

        private void ListenForINotifyPropertyChanged(IRibbonViewModel ribbonViewModel)
        {
            var notifiesOfPropertyChanged = ribbonViewModel as INotifyPropertyChanged;
            if (notifiesOfPropertyChanged != null)
            {
                notifiesOfPropertyChanged.PropertyChanged += NotifiesOfPropertyChangedPropertyChanged;
            }
        }

        void NotifiesOfPropertyChangedPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var viewModel = (IRibbonViewModel)sender;
            var senderType = sender.GetType();

            foreach (var invalidatedControl in
                notifyChangeTargetLookup[senderType]
                    .Where(property => property.Key == e.PropertyName)
                    .Select(pair => pair.Value)
                    .Distinct()
                    .Where(invalidatedControl => viewModel.RibbonUi != null))
            {
                VstoContribLog.Debug(_ => _("Invalidating {0} due to property change notification", invalidatedControl));
                viewModel.RibbonUi.InvalidateControl(invalidatedControl);
            }
        }

        private void CleanupViewModel(object context)
        {
            VstoContribLog.Debug(_ => _("Cleaning up viewmodel for context: {0}", context.ToLogFormat()));
            if (!contextToViewModelLookup.ContainsKey(context))
            {
                VstoContribLog.Warn(_ => _("Cannot find ViewModel to cleanup: {0}", context.ToLogFormat()));
                return;
            }

            var viewModelInstance = contextToViewModelLookup[context];
            VstoContribLog.Info(_ => _("ViewModel is {0}", viewModelInstance.ToLogFormat()));

            var notifyOfPropertyChanged = viewModelInstance as INotifyPropertyChanged;
            if (notifyOfPropertyChanged != null)
                notifyOfPropertyChanged.PropertyChanged -= NotifiesOfPropertyChangedPropertyChanged;

            viewModelInstance.Cleanup();
            vstoContribContext.ViewModelFactory.Release(viewModelInstance);
            contextToViewModelLookup.Remove(context);
        }

        public void RegisterCallbackControl(string ribbonType, string controlCallback, string ribbonControl)
        {
            var type = ribbonTypeLookup[ribbonType];
            if (!notifyChangeTargetLookup.ContainsKey(type))
                notifyChangeTargetLookup.Add(type, new List<KeyValuePair<string, string>>());

            notifyChangeTargetLookup[type].Add(new KeyValuePair<string, string>(controlCallback, ribbonControl));
        }

        public void Dispose()
        {
            var disposable = vstoContribContext.ViewModelFactory as IDisposable;
            if (disposable != null)
                disposable.Dispose();
        }
    }
}
