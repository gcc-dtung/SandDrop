using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GameKit.Core.Foundation.Logging;

namespace GameKit.UI.Framework
{
    /// <summary>
    /// Yes / no dialog that can be awaited:
    /// <code>
    /// bool quit = await popups.Get&lt;ConfirmPopup&gt;().AskAsync("Quit the level?", "Pause", "Quit", "Stay");
    /// </code>
    /// Closing it any other way (back key, backdrop, close button) answers false.
    /// Pass <c>cancel: null</c> for a single-button message box.
    /// </summary>
    public sealed class ConfirmPopup : PopupView
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private Button confirmButton;
        [SerializeField] private TMP_Text confirmLabel;
        [SerializeField] private Button cancelButton;
        [SerializeField] private TMP_Text cancelLabel;

        /// <summary>Title text (cheats hook a hidden triple tap on it).</summary>
        public TMP_Text TitleText => titleText;

        private bool answer;
        private bool wired;

        public async UniTask<bool> AskAsync(string message, string title = null, string confirm = "OK", string cancel = "Cancel",
            CancellationToken token = default)
        {
            if (IsOpen)
            {
                Log.Warning("ConfirmPopup", "Already open; the new question is ignored.", this);
                return false;
            }

            Wire();
            if (titleText != null)
            {
                titleText.text = title ?? string.Empty;
                titleText.gameObject.SetActive(!string.IsNullOrEmpty(title));
            }
            if (messageText != null) messageText.text = message;
            if (confirmLabel != null) confirmLabel.text = confirm;
            if (cancelButton != null) cancelButton.gameObject.SetActive(cancel != null);
            if (cancelLabel != null && cancel != null) cancelLabel.text = cancel;

            answer = false;
            Open();
            await WaitUntilClosed(token);
            return answer;
        }

        private void Wire()
        {
            if (wired)
            {
                return;
            }
            wired = true;

            if (confirmButton != null) confirmButton.onClick.AddListener(() => Answer(true));
            if (cancelButton != null) cancelButton.onClick.AddListener(() => Answer(false));
        }

        private void Answer(bool value)
        {
            if (!IsOpen)
            {
                return;
            }
            answer = value;
            Close();
        }
    }
}
