using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    internal sealed class EditorPixelJob : IDisposable
    {
        readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        readonly Action<byte[]> _complete;
        readonly Action<Exception> _error;
        readonly Action _cancelled, _finished, _repaint;
        Task<byte[]> _task;
        int _progressId = -1, _percent, _reported = -1;
        bool _closed;

        internal int Percent => Volatile.Read(ref _percent);
        internal string Title { get; }
        internal void Cancel() { if (!_closed) _cancel.Cancel(); }

        internal EditorPixelJob(string title, Func<CancellationToken, Func<float, bool>, byte[]> calculate,
            Action<byte[]> complete, Action<Exception> error, Action cancelled, Action finished, Action repaint)
            : this(title, (token, progress) => Task.Run(() => calculate(token, progress), token),
                  complete, error, cancelled, finished, repaint) { }

        internal EditorPixelJob(string title, Func<CancellationToken, Task<Color32[]>> capture,
            Func<Color32[], CancellationToken, Func<float, bool>, byte[]> calculate,
            Action<byte[]> complete, Action<Exception> error, Action cancelled, Action finished, Action repaint)
            : this(title, async (token, progress) =>
            {
                var pixels = await capture(token).ConfigureAwait(false);
                return await Task.Run(() => calculate(pixels, token, progress), token).ConfigureAwait(false);
            }, complete, error, cancelled, finished, repaint) { }

        EditorPixelJob(string title, Func<CancellationToken, Func<float, bool>, Task<byte[]>> start,
            Action<byte[]> complete, Action<Exception> error, Action cancelled, Action finished, Action repaint)
        {
            Title = title;
            _complete = complete;
            _error = error;
            _cancelled = cancelled;
            _finished = finished;
            _repaint = repaint;
            try
            {
                _progressId = Progress.Start(title);
                Progress.RegisterCancelCallback(_progressId, () => { Cancel(); return true; });
                var token = _cancel.Token;
                _task = start(token, f =>
                {
                    Volatile.Write(ref _percent, (int)(f * 100));
                    return token.IsCancellationRequested;
                });
                EditorApplication.update += Tick;
            }
            catch { Dispose(); throw; }
        }

        internal void Tick()
        {
            if (_closed) return;
            if (!_task.IsCompleted)
            {
                int percent = Percent;
                if (percent != _reported && !_cancel.IsCancellationRequested)
                {
                    _reported = percent;
                    Progress.Report(_progressId, percent / 100f);
                    _repaint();
                }
                return;
            }
            bool cancelled = _cancel.IsCancellationRequested || _task.IsCanceled;
            CloseProgress();
            try
            {
                if (cancelled) _cancelled();
                else _complete(_task.GetAwaiter().GetResult());
            }
            catch (Exception error) { _error(error); }
            finally
            {
                if (_task.IsFaulted) _ = _task.Exception;
                _cancel.Dispose();
                _finished();
                _repaint();
            }
        }

        void CloseProgress()
        {
            _closed = true;
            EditorApplication.update -= Tick;
            if (_progressId >= 0 && Progress.Exists(_progressId)) Progress.Remove(_progressId);
        }

        public void Dispose()
        {
            if (_closed) return;
            CloseProgress();
            _cancel.Cancel();
            if (_task != null && !_task.IsCompleted)
                _task.ContinueWith(done => { _ = done.Exception; _cancel.Dispose(); }, TaskScheduler.Default);
            else
            {
                if (_task?.IsFaulted == true) _ = _task.Exception;
                _cancel.Dispose();
            }
            _finished();
        }
    }
}
