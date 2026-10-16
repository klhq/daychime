// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Windows.Widgets.Providers;
using System;
using Windows.Storage;

namespace DaychimeWidget
{
    internal delegate WidgetImplBase WidgetCreateDelegate(string widgetId, string initialState);
    internal abstract class WidgetImplBase
    {
        protected string id;
        protected string state;
        protected bool isActivated = false;
        private string lastSentData;
        private string lastSentState;

        protected WidgetImplBase(string widgetId, string initialState)
        {
            state = initialState;
            id = widgetId;
        }

        public string Id { get => id; }
        public string State { get => state; }
        public bool IsActivated { get => isActivated; }

        protected static string ReadPackageFileFromUri(string packageUri)
        {
            var uri = new Uri(packageUri);
            var storageFileTask = StorageFile.GetFileFromApplicationUriAsync(uri).AsTask();
            storageFileTask.Wait();

            var readTextTask = FileIO.ReadTextAsync(storageFileTask.Result).AsTask();
            readTextTask.Wait();

            return readTextTask.Result;
        }

        // Sends the card to the host. With skipIfUnchanged, an update identical to the last one
        // is not sent: the host keeps the card it has, and resending it redraws the card, which
        // flashes when the Widgets Board re-activates the widget after Customize widget.
        public void SendUpdate(bool includeTemplate = false, bool skipIfUnchanged = false)
        {
            var data = GetDataForWidget();
            if (skipIfUnchanged && !includeTemplate && data == lastSentData && state == lastSentState) return;
            var options = new WidgetUpdateRequestOptions(id) { Data = data, CustomState = state };
            if (includeTemplate) options.Template = GetTemplateForWidget();
            WidgetManager.GetDefault().UpdateWidget(options);
            lastSentData = data;
            lastSentState = state;
        }

        public virtual void Activate(WidgetContext widgetContext) { isActivated = true; }
        public virtual void Deactivate() { isActivated = false; }
        public virtual void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs) { }
        public virtual void OnWidgetOpened() { }
        public virtual void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs) { }
        public virtual void OnSessionUnlock() { }

        public abstract string GetTemplateForWidget();
        public abstract string GetDataForWidget();
    }
}
