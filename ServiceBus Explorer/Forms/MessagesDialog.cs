using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ServiceBusExplorer.Localization;
using ServiceBusExplorer.Models;
using ServiceBusExplorer.UI;
using ServiceBusExplorer.UI.Controls;

namespace ServiceBusExplorer.Forms
{
    /// <summary>
    /// Messages sent by one or more steps, read from the queue or the subscriptions of their endpoint, dead-letter queue included.
    /// The read starts when the popup opens (the user asked for it) and can be stopped or run again.
    /// </summary>
    [DesignerCategory("Code")]
    public class MessagesDialog : ThemedDialog
    {
        /// <summary>Reads the batches (active messages and dead-letter queue): progress, stop.</summary>
        public delegate List<MessageBatch> Reader(Action<string> progress, CancellationToken token);

        private readonly MessagesPanel _panel;
        private readonly Reader _reader;
        private CancellationTokenSource _stop;

        public MessagesDialog(string title, string subtitle, string intro, HashSet<Guid> steps, Reader reader)
            : base(title, subtitle)
        {
            _reader = reader;
            Size = new Size(1280, 800);
            MinimumSize = new Size(900, 560);
            Resizable = true;

            _panel = new MessagesPanel { Dock = DockStyle.Fill };
            _panel.SetContext(intro, true, steps);
            _panel.ReadRequested += async (s, e) => await ReadAsync();
            _panel.StopRequested += (s, e) => _stop?.Cancel();

            var card = new CardPanel { Dock = DockStyle.Fill };
            card.Controls.Add(_panel);
            Body.Controls.Add(card);

            var close = AddFooterButton(Strings.Close, ButtonVariant.Primary, DialogResult.OK);
            AcceptButton = close;
            CancelButton = close;

            Shown += async (s, e) => await ReadAsync();
            FormClosing += (s, e) => _stop?.Cancel();
        }

        internal MessagesPanel Panel => _panel;

        private async Task ReadAsync()
        {
            if (_stop != null) return;
            var stop = new CancellationTokenSource();
            _stop = stop;
            _panel.Busy = true;
            var progress = new Progress<string>(text =>
            {
                if (!IsDisposed) _panel.ShowProgress(text);
            });

            try
            {
                var batches = await Task.Run(() => _reader(text => ((IProgress<string>)progress).Report(text), stop.Token));
                if (!IsDisposed && !stop.IsCancellationRequested) _panel.SetBatches(batches);
            }
            catch (OperationCanceledException)
            {
                // Stopped by the user, or the popup was closed.
            }
            catch (Exception error)
            {
                if (!IsDisposed) MessageDialog.ShowError(this, error);
            }
            finally
            {
                _stop = null;
                stop.Dispose();
                if (!IsDisposed) _panel.Busy = false;
            }
        }
    }
}
