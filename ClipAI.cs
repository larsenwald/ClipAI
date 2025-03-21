using System;
using System.Drawing;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace ClipAI
{
    public partial class MainForm : Form
    {
        private NotifyIcon trayIcon;
        private HttpClient httpClient;
        private string apiKey;
        private string preprompt = "";
        private bool covertMode = false;
        private bool isProcessing = false;
        private GlobalKeyboardHook keyboardHook;

        // For clipboard monitoring
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int MOD_ALT = 0x0001;
        private const int MOD_CONTROL = 0x0002;
        private const int VK_C = 0x43;
        private const int HOTKEY_ID = 1;

        public MainForm()
        {
            InitializeComponent();
            InitializeTrayIcon();
            this.Icon = new Icon("Assets/ClipAI.ico");
            httpClient = new HttpClient();
            keyboardHook = new GlobalKeyboardHook();
        }

        private void InitializeComponent()
        {
            this.ClientSize = new System.Drawing.Size(400, 320);
            this.Text = "ClipAI";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormClosing += MainForm_FormClosing;
            this.Load += MainForm_Load;

            // API key label and textbox
            Label apiKeyLabel = new Label
            {
                Text = "Enter your Gemini API key:",
                Location = new Point(20, 20),
                Size = new Size(360, 20)
            };
            this.Controls.Add(apiKeyLabel);

            TextBox apiKeyTextBox = new TextBox
            {
                Location = new Point(20, 50),
                Size = new Size(360, 20),
                Name = "apiKeyTextBox"
            };
            this.Controls.Add(apiKeyTextBox);

            // Preprompt label and textbox
            Label prepromptLabel = new Label
            {
                Text = "Optional preprompt (added before each request):",
                Location = new Point(20, 90),
                Size = new Size(360, 20)
            };
            this.Controls.Add(prepromptLabel);

            TextBox prepromptTextBox = new TextBox
            {
                Location = new Point(20, 120),
                Size = new Size(360, 60),
                Name = "prepromptTextBox",
                Multiline = true,
                ScrollBars = ScrollBars.Vertical
            };

            // Set placeholder text (this will be visible when the textbox is empty)
            prepromptTextBox.Text = "e.g. \"Please format your response as plain text without markdown, bullet points, or other formatting.\"";
            prepromptTextBox.ForeColor = Color.Gray;

            // Add event handlers for placeholder behavior
            prepromptTextBox.Enter += (s, e) =>
            {
                if (prepromptTextBox.ForeColor == Color.Gray)
                {
                    prepromptTextBox.Text = "";
                    prepromptTextBox.ForeColor = Color.Black;
                }
            };

            prepromptTextBox.Leave += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(prepromptTextBox.Text))
                {
                    prepromptTextBox.Text = "e.g. \"Please format your response as plain text without markdown, bullet points, or other formatting.\"";
                    prepromptTextBox.ForeColor = Color.Gray;
                }
            };

            this.Controls.Add(prepromptTextBox);

            // Covert mode checkbox
            CheckBox covertModeCheckBox = new CheckBox
            {
                Text = "Covert mode (no notifications)",
                Location = new Point(20, 190),
                Size = new Size(360, 20),
                Name = "covertModeCheckBox"
            };
            this.Controls.Add(covertModeCheckBox);

            // Submit button
            Button submitButton = new Button
            {
                Text = "Submit",
                Location = new Point(160, 220),
                Size = new Size(80, 30),
                Name = "submitButton"
            };
            submitButton.Click += SubmitButton_Click;
            this.Controls.Add(submitButton);

            // Status label
            Label statusLabel = new Label
            {
                Text = "ClipAI: Press Ctrl+Alt+C to process clipboard text with Gemini AI",
                Location = new Point(20, 260),
                Size = new Size(360, 40),
                TextAlign = ContentAlignment.MiddleCenter,
                Name = "statusLabel"
            };
            this.Controls.Add(statusLabel);
        }

        private void InitializeTrayIcon()
        {
            trayIcon = new NotifyIcon
            {
                Icon = new Icon("Assets/ClipAI.ico"),
                Visible = false,
                Text = "ClipAI"
            };

            // Create context menu for tray icon
            ContextMenuStrip contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Show", null, ShowForm);
            contextMenu.Items.Add("Exit", null, Exit);
            trayIcon.ContextMenuStrip = contextMenu;
            trayIcon.DoubleClick += ShowForm;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // Load any saved API key here if implemented
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && apiKey != null)
            {
                e.Cancel = true;
                this.Hide();
                trayIcon.Visible = true;
            }
        }

        private async void SubmitButton_Click(object sender, EventArgs e)
        {
            TextBox apiKeyTextBox = (TextBox)this.Controls["apiKeyTextBox"];
            TextBox prepromptTextBox = (TextBox)this.Controls["prepromptTextBox"];
            CheckBox covertModeCheckBox = (CheckBox)this.Controls["covertModeCheckBox"];
            Button submitButton = (Button)this.Controls["submitButton"];
            Label statusLabel = (Label)this.Controls["statusLabel"];

            apiKey = apiKeyTextBox.Text.Trim();
            if (string.IsNullOrEmpty(apiKey))
            {
                MessageBox.Show("Please enter your Gemini API key.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Save preprompt value (if not placeholder)
            if (prepromptTextBox.ForeColor != Color.Gray)
            {
                preprompt = prepromptTextBox.Text.Trim();
            }
            else
            {
                preprompt = "";
            }

            // Save covert mode setting
            covertMode = covertModeCheckBox.Checked;

            submitButton.Enabled = false;
            statusLabel.Text = "Verifying API key...";

            // Create a simple loading animation
            using (var loadingForm = new Form
            {
                Size = new Size(200, 70),
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.WhiteSmoke
            })
            {
                var loadingLabel = new Label
                {
                    Text = "Verifying...",
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Dock = DockStyle.Fill
                };
                loadingForm.Controls.Add(loadingLabel);

                // Show loading form while testing API key
                loadingForm.Show(this);

                bool isKeyValid = await TestApiKey(apiKey);

                loadingForm.Close();

                if (isKeyValid)
                {
                    statusLabel.Text = "Key locked in. Settings can still be edited.";
                    statusLabel.ForeColor = Color.Green;
                    submitButton.Enabled = true; // Re-enable submit button

                    // Register global hotkey
                    RegisterHotKey(this.Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_C);

                    // Enable minimizing to tray
                    this.WindowState = FormWindowState.Minimized;
                    this.Hide();
                    trayIcon.Visible = true;

                    // Show a brief notification (unless in covert mode)
                    if (!covertMode)
                    {
                        ShowTooltip("ClipAI is running in the background");
                    }
                }
                else
                {
                    statusLabel.Text = "Invalid API key. Please try again.";
                    statusLabel.ForeColor = Color.Red;
                    submitButton.Enabled = true;
                    apiKey = null;
                }
            }
        }

        private async Task<bool> TestApiKey(string key)
        {
            try
            {
                string testUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={key}";
                var testContent = new
                {
                    contents = new[]
                    {
                        new { role = "user", parts = new[] { new { text = "Hello" } } }
                    }
                };

                var jsonContent = JsonSerializer.Serialize(testContent);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync(testUrl, httpContent);

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_HOTKEY = 0x0312;

            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                if (!isProcessing)
                {
                    ProcessClipboard();
                }
            }

            base.WndProc(ref m);
        }

        private async void ProcessClipboard()
        {
            if (isProcessing) return;

            try
            {
                isProcessing = true;

                // Check if clipboard contains text
                if (!Clipboard.ContainsText())
                {
                    if (!covertMode) ShowTooltip("No text in clipboard", isError: true);
                    isProcessing = false;
                    return;
                }

                string clipboardText = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(clipboardText))
                {
                    if (!covertMode) ShowTooltip("Empty text in clipboard", isError: true);
                    isProcessing = false;
                    return;
                }

                // Show processing notification (unless in covert mode)
                if (!covertMode) ShowTooltip("Processing clipboard text...");

                // Add preprompt if specified
                string finalPrompt = clipboardText;
                if (!string.IsNullOrEmpty(preprompt))
                {
                    finalPrompt = preprompt + "\n\n" + clipboardText;
                }

                // Create request to Gemini API
                string apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={apiKey}";
                var requestContent = new
                {
                    contents = new[]
                    {
                        new { role = "user", parts = new[] { new { text = finalPrompt } } }
                    }
                };

                var jsonContent = JsonSerializer.Serialize(requestContent);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync(apiUrl, httpContent);

                if (response.IsSuccessStatusCode)
                {
                    string responseJson = await response.Content.ReadAsStringAsync();
                    var responseData = JsonDocument.Parse(responseJson);

                    // Extract text from response (based on API structure)
                    string aiResponse = responseData.RootElement
                        .GetProperty("candidates")[0]
                        .GetProperty("content")
                        .GetProperty("parts")[0]
                        .GetProperty("text")
                        .GetString();

                    // Set the response to clipboard
                    Clipboard.SetText(aiResponse);
                    if (!covertMode) ShowTooltip("AI response copied to clipboard");
                }
                else
                {
                    if (!covertMode) ShowTooltip("Error: Failed to process with API", isError: true);
                }
            }
            catch (Exception ex)
            {
                if (!covertMode) ShowTooltip($"Error: {ex.Message}", isError: true);
            }
            finally
            {
                isProcessing = false;
            }
        }

        private void ShowTooltip(string message, bool isError = false)
        {
            // Create a custom tooltip instead of standard Windows notification
            Form tooltipForm = new Form
            {
                Size = new Size(300, 40),
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                TopMost = true,
                BackColor = isError ? Color.FromArgb(255, 200, 200) : Color.FromArgb(220, 255, 220)
            };

            // Position near system tray
            Rectangle workingArea = Screen.GetWorkingArea(this);
            tooltipForm.Location = new Point(workingArea.Right - tooltipForm.Width - 10, workingArea.Bottom - tooltipForm.Height - 10);

            Label messageLabel = new Label
            {
                Text = message,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = isError ? Color.DarkRed : Color.DarkGreen
            };
            tooltipForm.Controls.Add(messageLabel);

            tooltipForm.Show();

            // Auto-close after 2 seconds - explicitly use Windows.Forms.Timer to fix ambiguity
            var timer = new System.Windows.Forms.Timer { Interval = 2000 };
            timer.Tick += (s, e) =>
            {
                tooltipForm.Close();
                timer.Dispose();
            };
            timer.Start();
        }

        private void ShowForm(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
            trayIcon.Visible = false;

            // Restore form controls to editable state
            RestoreFormControls();
        }

        private void RestoreFormControls()
        {
            // Enable submit button
            Button submitButton = (Button)this.Controls["submitButton"];
            submitButton.Enabled = true;

            // Populate current values in the form controls
            TextBox apiKeyTextBox = (TextBox)this.Controls["apiKeyTextBox"];
            apiKeyTextBox.Text = apiKey ?? "";

            TextBox prepromptTextBox = (TextBox)this.Controls["prepromptTextBox"];
            if (!string.IsNullOrEmpty(preprompt))
            {
                prepromptTextBox.Text = preprompt;
                prepromptTextBox.ForeColor = Color.Black;
            }
            else if (prepromptTextBox.ForeColor != Color.Gray)
            {
                // Reset to placeholder if no preprompt is set
                prepromptTextBox.Text = "e.g. \"Please format your response as plain text without markdown, bullet points, or other formatting.\"";
                prepromptTextBox.ForeColor = Color.Gray;
            }

            CheckBox covertModeCheckBox = (CheckBox)this.Controls["covertModeCheckBox"];
            covertModeCheckBox.Checked = covertMode;

            // Update status label
            Label statusLabel = (Label)this.Controls["statusLabel"];
            if (apiKey != null)
            {
                statusLabel.Text = "ClipAI is running. You can edit settings and resubmit.";
                statusLabel.ForeColor = Color.Green;
            }
            else
            {
                statusLabel.Text = "ClipAI: Press Ctrl+Alt+C to process clipboard text with Gemini AI";
                statusLabel.ForeColor = SystemColors.ControlText;
            }
        }

        private void Exit(object sender, EventArgs e)
        {
            trayIcon.Visible = false;
            UnregisterHotKey(this.Handle, HOTKEY_ID);
            Application.Exit();
        }
    }

    // Global keyboard hook helper class
    public class GlobalKeyboardHook
    {
        // Implementation would go here if needed for more complex hotkey handling
        // For this app, we're using the simpler WM_HOTKEY approach
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}