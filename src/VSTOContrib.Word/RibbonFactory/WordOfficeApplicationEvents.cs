using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Office.Interop.Word;
using VSTOContrib.Core;
using VSTOContrib.Core.Extensions;
using VSTOContrib.Core.RibbonFactory;
using VSTOContrib.Core.RibbonFactory.Interfaces;
using VSTOContrib.Core.RibbonFactory.Internal;

namespace VSTOContrib.Word.RibbonFactory
{
    public class WordOfficeApplicationEvents : IOfficeApplicationEvents
    {
        const string CaptionSuffix = " - Word";
        const string WordLpClassName = "OpusApp";
        private readonly List<int> closedDocuments = new List<int>();
        private readonly Dictionary<Document, List<OfficeWin32Window>> documents;
        private readonly Dictionary<Document, DocumentWrapper> documentWrappers;
        private Application wordApplication;

        /// <summary>
        /// Initializes a new instance of the <see cref="WordOfficeApplicationEvents"/> class.
        /// </summary>
        public WordOfficeApplicationEvents()
        {
            documentWrappers = new Dictionary<Document, DocumentWrapper>();
            documents = new Dictionary<Document, List<OfficeWin32Window>>();
        }

        void WordApplicationWindowActivate(Document doc, Window wn)
        {
            VstoContribLog.Info(_ => _("Application.WindowActivate raised, Document: {0}, Window: {1}", 
                doc.ToLogFormat(), wn.ToLogFormat()));
            if (!documents.ContainsKey(doc))
            {
                documents.Add(doc, new List<OfficeWin32Window>());
                var documentWrapper = new DocumentWrapper(doc);
                documentWrapper.Closed += DocumentClosed;
                documentWrappers.Add(doc, documentWrapper);
            }

            var officeWin32Window = new OfficeWin32Window(wn, WordLpClassName, CaptionSuffix);
            //Check if we have this window registered
            if (documents[doc].Any(window => window.Equals(officeWin32Window))) return;

            documents[doc].Add(officeWin32Window);
            NewView(new NewViewEventArgs(officeWin32Window, doc, WordRibbonType.WordDocument.GetEnumDescription()));
        }

        void DocumentClosed(object sender, DocumentClosedEventArgs e)
        {
            var document = e.Document;
            CleanupDocument(document);
        }

        void CleanupDocument(Document document)
        {
            if (!documentWrappers.ContainsKey(document)) return;

            closedDocuments.Add(document.GetHashCode());
            var documentWrapper = documentWrappers[document];
            documentWrapper.Closed -= DocumentClosed;
            documentWrappers.Remove(document);
            var windows = documents[document];

            foreach (var window in windows)
            {
                ViewClosed(window);
                window.ReleaseComObject();
            }
            documents.Remove(document);
            if (wordApplication.Documents.Count == 1)
            {
                foreach (var viewInstance in wordApplication.Windows)
                {
                    var officeWin32Window = new OfficeWin32Window(viewInstance, WordLpClassName, CaptionSuffix);
                    var enumDescription = WordRibbonType.WordDocument.GetEnumDescription();
                    NewView(new NewViewEventArgs(officeWin32Window, null, enumDescription));
                }
            }
        }

        public void Initialise(object application)
        {
            wordApplication = (Application) application;
            wordApplication.WindowActivate += WordApplicationWindowActivate;
            wordApplication.DocumentOpen += WordApplicationDocumentOpen;
            wordApplication.DocumentChange += WordApplicationOnDocumentChange;
            //TODO protected window activate
        }

        public event Action<NewViewEventArgs> NewView;
        public event Action<OfficeWin32Window> ViewClosed;
        public event Action<object> ContextClosed;

        void WordApplicationOnDocumentChange()
        {
            var enumDescription = WordRibbonType.WordDocument.GetEnumDescription();
            if (wordApplication.Documents.Count == 0)
            {
                VstoContribLog.Debug(_ => _("Application.DocumentChange raised, no documents currently open"));
                foreach (var viewInstance in wordApplication.Windows)
                {
                    NewView(new NewViewEventArgs(new OfficeWin32Window(viewInstance, WordLpClassName, CaptionSuffix), null, enumDescription));
                }
            }
            else
            {
                var activeDocument = wordApplication.ActiveDocument;
                if (closedDocuments.Contains(activeDocument.GetHashCode()))
                {
                    VstoContribLog.Debug(_ => _("Application.DocumentChange raised ActiveDocument: {0} is closing, ignoring event", activeDocument.ToLogFormat()));
                    return;
                }
                var activeWindow = wordApplication.ActiveWindow;
                VstoContribLog.Debug(_ => _("Application.DocumentChange raised, ActiveDocument: {0}, ActiveWindow: {1}",
                    activeDocument.ToLogFormat(), activeWindow.ToLogFormat()));
                NewView(new NewViewEventArgs(new OfficeWin32Window(activeWindow, WordLpClassName, CaptionSuffix), activeDocument, enumDescription));
            }
        }

        void WordApplicationDocumentOpen(Document doc)
        {
            VstoContribLog.Debug(_ => _("Application.DocumentOpen raised, Document: {0}", doc.ToLogFormat()));
            WordApplicationWindowActivate(doc, doc.ActiveWindow);
        }

        public OfficeWin32Window ToOfficeWindow(object view)
        {
            return new OfficeWin32Window(view, WordLpClassName, CaptionSuffix);
        }

        public void Dispose()
        {
            wordApplication.WindowActivate -= WordApplicationWindowActivate;
            wordApplication.DocumentOpen -= WordApplicationDocumentOpen;
            wordApplication.DocumentChange -= WordApplicationOnDocumentChange;
            wordApplication = null;
        }

        public void RegisterOpenDocuments()
        {
            VstoContribLog.Debug(_ => _("Registering all already open documents"));
            using (var documents = wordApplication.Documents.WithComCleanup())
            {
                foreach (Document document in documents.Resource)
                {
                    using (var windows = document.Windows.WithComCleanup())
                    {
                        foreach (Window window in windows.Resource)
                        {
                            WordApplicationWindowActivate(document, window);
                        }
                    }
                }
            }
        }
    }
}