using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using EasyHook;

namespace RealAcceptanceControl
{
    public sealed class ControlEntryPoint : IEntryPoint
    {
        private readonly string resultPath;

        public ControlEntryPoint(RemoteHooking.IContext context, string channelName)
        {
            this.resultPath = Path.Combine(
                Path.GetDirectoryName(typeof(ControlEntryPoint).Assembly.Location) ?? string.Empty,
                "control-result.txt");
        }

        public void Run(RemoteHooking.IContext context, string channelName)
        {
            try
            {
                Form socketForm = FindSocketForm();
                if (socketForm == null)
                {
                    WriteResult("socket-form-found=false");
                    return;
                }

                MethodInfo startHook = socketForm.GetType().GetMethod(
                    "StartHook_MainForm",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (startHook == null)
                {
                    WriteResult("start-hook-method-found=false");
                    return;
                }

                Exception invokeError = null;
                using (ManualResetEvent completed = new ManualResetEvent(false))
                {
                    socketForm.BeginInvoke((MethodInvoker)(() =>
                    {
                        try
                        {
                            startHook.Invoke(socketForm, null);
                        }
                        catch (Exception ex)
                        {
                            invokeError = ex;
                        }
                        finally
                        {
                            completed.Set();
                        }
                    }));

                    if (!completed.WaitOne(TimeSpan.FromSeconds(20)))
                    {
                        WriteResult("start-hook-completed=false;reason=timeout");
                        return;
                    }
                }

                if (invokeError != null)
                {
                    WriteResult("start-hook-completed=false;error=" + FlattenException(invokeError));
                    return;
                }

                WriteResult("socket-form-found=true");
                WriteResult("start-hook-invoked=true");
                WriteResult("hook-running=" + ReadHookRunning(socketForm));
            }
            catch (Exception ex)
            {
                WriteResult("control-error=" + FlattenException(ex));
            }
        }

        private static Form FindSocketForm()
        {
            foreach (Form form in Application.OpenForms)
            {
                if (string.Equals(
                    form.GetType().FullName,
                    "WPELibrary.Socket_Form",
                    StringComparison.Ordinal))
                {
                    return form;
                }
            }

            return null;
        }

        private static bool ReadHookRunning(Form socketForm)
        {
            FieldInfo hookField = socketForm.GetType().GetField(
                "ws",
                BindingFlags.Instance | BindingFlags.NonPublic);
            object hook = hookField == null ? null : hookField.GetValue(socketForm);
            PropertyInfo isRunning = hook == null
                ? null
                : hook.GetType().GetProperty("IsRunning", BindingFlags.Instance | BindingFlags.Public);
            return isRunning != null && (bool)isRunning.GetValue(hook, null);
        }

        private void WriteResult(string line)
        {
            try
            {
                File.AppendAllText(this.resultPath, line + Environment.NewLine);
            }
            catch
            {
                // The UI state remains the authoritative verification signal.
            }
        }

        private static string FlattenException(Exception exception)
        {
            string message = exception.Message;
            Exception inner = exception.InnerException;
            while (inner != null)
            {
                message += " | " + inner.Message;
                inner = inner.InnerException;
            }

            return message.Replace(Environment.NewLine, " ");
        }
    }
}
