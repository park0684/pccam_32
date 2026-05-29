using System;
using System.Drawing;
using System.Windows.Forms;

namespace pccam_32.Views
{
    /// <summary>
    /// 시스템 트레이 기반 ApplicationContext.
    /// 
    /// 메뉴 구성:
    /// 1. 인증상태
    /// 2. 설정
    /// 3. 종료
    /// </summary>
    public class TrayApplicationContext : ApplicationContext, ITrayView
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _contextMenu;

        private readonly ToolStripMenuItem _authStatusMenuItem;
        private readonly ToolStripMenuItem _settingMenuItem;
        private readonly ToolStripMenuItem _exitMenuItem;

        private readonly Form _invokerForm;

        public event EventHandler SettingRequested;
        public event EventHandler ExitRequested;

        public TrayApplicationContext()
        {
            _invokerForm = new Form();
            _invokerForm.ShowInTaskbar = false;
            _invokerForm.WindowState = FormWindowState.Minimized;
            _invokerForm.Load += delegate
            {
                _invokerForm.Visible = false;
            };

            _authStatusMenuItem = new ToolStripMenuItem("인증상태: 확인 중");
            _authStatusMenuItem.Enabled = false;

            _settingMenuItem = new ToolStripMenuItem("설정");
            _exitMenuItem = new ToolStripMenuItem("종료");

            _contextMenu = new ContextMenuStrip();
            _contextMenu.Items.Add(_authStatusMenuItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(_settingMenuItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(_exitMenuItem);

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = SystemIcons.Application;
            _notifyIcon.Text = "PC CAM";
            _notifyIcon.Visible = true;
            _notifyIcon.ContextMenuStrip = _contextMenu;

            _settingMenuItem.Click += delegate { RaiseEvent(SettingRequested); };
            _exitMenuItem.Click += delegate { RaiseEvent(ExitRequested); };

            _notifyIcon.DoubleClick += delegate
            {
                RaiseEvent(SettingRequested);
            };
        }

        private void RaiseEvent(EventHandler handler)
        {
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        public void SetAuthStatusText(string text)
        {
            RunOnUiThread(delegate
            {
                if (string.IsNullOrWhiteSpace(text))
                    text = "확인 중";

                _authStatusMenuItem.Text = "인증상태: " + text;
            });
        }

        public void SetStatusText(string text)
        {
            RunOnUiThread(delegate
            {
                string safeText = string.IsNullOrWhiteSpace(text)
                    ? "PC CAM"
                    : text;

                if (safeText.Length > 60)
                    safeText = safeText.Substring(0, 60);

                _notifyIcon.Text = safeText;
            });
        }

        public void ShowInfo(string message)
        {
            RunOnUiThread(delegate
            {
                MessageBox.Show(
                    message,
                    "PC CAM",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            });
        }

        public void ShowError(string message)
        {
            RunOnUiThread(delegate
            {
                MessageBox.Show(
                    message,
                    "PC CAM 오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            });
        }

        public string PromptPassword(string title, string message)
        {
            string result = null;

            RunOnUiThread(delegate
            {
                using (PasswordPromptForm form = new PasswordPromptForm(title, message))
                {
                    if (form.ShowDialog() == DialogResult.OK)
                        result = form.Password;
                }
            });

            return result;
        }

        public void CloseView()
        {
            RunOnUiThread(delegate
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();

                if (_contextMenu != null)
                    _contextMenu.Dispose();

                if (_invokerForm != null)
                    _invokerForm.Dispose();

                ExitThread();
            });
        }

        private void RunOnUiThread(Action action)
        {
            if (action == null)
                return;

            if (_invokerForm.IsHandleCreated && _invokerForm.InvokeRequired)
            {
                try
                {
                    _invokerForm.Invoke(action);
                }
                catch
                {
                }
            }
            else
            {
                action();
            }
        }
    }
}