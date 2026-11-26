using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpineRecovery.App
{
    public sealed partial class AppShellUi
    {
        private GameObject CreateLoginPanel(Transform parent)
        {
            var content = CreateAuthPage(parent, "LoginPanel", out var panel, out var card);
            AuthText(card, "欢迎回来", 24, 24, 302, 36, 24, true);
            AuthText(card, "请登录以继续您的康复旅程", 24, 68, 302, 28, 15, false, QuestionnaireUi.Muted);
            accountInput = AuthInput(card, "手机号 / 邮箱", 120, false);
            passwordInput = AuthInput(card, "密码", 194, true);
            loginMessage = AuthText(card, "", 24, 264, 302, 42, 13, false, QuestionnaireUi.DangerText);
            var login = AuthButton(card, "登录", 322, HandleLoginClicked);
            var register = AuthButton(card, "还没有账号？立即注册", 388,
                () => bootstrap.Router.Navigate(AppRoute.Register), false);
            AuthLayout(content, card, loginMessage, login, register, 264);
            return panel;
        }

        private GameObject CreateRegisterPanel(Transform parent)
        {
            var content = CreateAuthPage(parent, "RegisterPanel", out var panel, out var card);
            AuthText(card, "创建账号", 24, 24, 302, 36, 24, true);
            AuthText(card, "注册后绑定医生为您创建的康复档案", 24, 68, 302, 42, 15, false, QuestionnaireUi.Muted);
            registerAccountInput = AuthInput(card, "设置账号", 126, false);
            registerPasswordInput = AuthInput(card, "设置密码", 200, true);
            registerConfirmInput = AuthInput(card, "再次输入密码", 274, true);
            var message = AuthText(card, "", 24, 344, 302, 0, 13);
            var submit = AuthButton(card, "注册并继续激活", 360, HandleRegisterClicked);
            var back = AuthButton(card, "已有账号？返回登录", 426,
                () => bootstrap.Router.Navigate(AppRoute.Login), false);
            AuthLayout(content, card, message, submit, back, 344);
            return panel;
        }

        private GameObject CreateActivationPanel(Transform parent)
        {
            var content = CreateAuthPage(parent, "ActivationPanel", out var panel, out var card);
            AuthText(card, "激活档案", 24, 24, 302, 36, 24, true);
            AuthText(card, "输入医生建档时使用的手机号，\n开始您的康复训练", 24, 68, 302, 48, 15, false, QuestionnaireUi.Muted);
            var info = QuestionnaireUi.Box(card, "ActivationInfo", new Color(.945f, .980f, 1f), 16, 24, 136, 302, 82);
            AuthText(info.transform, "绑定医生为您创建的康复档案", 16, 12, 270, 26, 15, true);
            AuthText(info.transform, "激活后同步训练计划和医生处方", 16, 42, 270, 26, 13, false, QuestionnaireUi.Muted);
            activationCodeInput = AuthInput(card, "请输入 11 位手机号", 242, false);
            activationCodeInput.keyboardType = TouchScreenKeyboardType.NumberPad;
            activationCodeInput.characterValidation = InputField.CharacterValidation.Integer;
            activationCodeInput.characterLimit = 11;
            activationCodeInput.onValueChanged.AddListener(_ => { previewActivationCode = null; activationMessage.text = ""; });
            AuthText(card, "手机号须与医生端患者档案一致", 24, 312, 302, 26, 13, false, QuestionnaireUi.Muted);
            activationMessage = AuthText(card, "", 24, 354, 302, 48, 14, false, QuestionnaireUi.Accent);
            var submit = AuthButton(card, "确认绑定", 422, HandleActivationClicked);
            var back = AuthButton(card, "返回登录", 488, () => bootstrap.State.ClearSession(), false);
            AuthLayout(content, card, activationMessage, submit, back, 354);
            return panel;
        }

        private static RectTransform CreateAuthPage(Transform parent, string name, out GameObject panel, out RectTransform card)
        {
            var root = QuestionnaireUi.CreateScreen(parent, name, null, out panel);
            var content = QuestionnaireUi.ScrollArea(root, 0, 0, 820);
            var texture = Resources.Load<Texture2D>("AppUi/MetaSpineMark");
            if (texture != null)
            {
                var mark = new GameObject("BrandMark", typeof(RectTransform), typeof(Image));
                mark.transform.SetParent(content, false);
                QuestionnaireUi.Place((RectTransform)mark.transform, 159, 28, 72, 64);
                var image = mark.GetComponent<Image>();
                image.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
                image.preserveAspect = true;
                image.raycastTarget = false;
            }
            AuthText(content, "MetaSpine", 20, 102, 350, 36, 26, true, QuestionnaireUi.Ink, TextAnchor.MiddleCenter);
            AuthText(content, "腰椎康复系统", 20, 142, 350, 28, 15, false, QuestionnaireUi.Muted, TextAnchor.MiddleCenter);
            card = QuestionnaireUi.Box(content, "AuthCard", Color.white, 24, 20, 198, 350, 580).rectTransform;
            return content;
        }

        private static Text AuthText(Transform parent, string value, float x, float y, float width, float height,
            float size, bool bold = false, Color? color = null, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var text = MobileUi.Text(parent, value, x, y, width, height, size, bold, color, alignment);
            return text;
        }

        private static InputField AuthInput(Transform parent, string placeholder, float y, bool password)
        {
            var image = QuestionnaireUi.Box(parent, password ? "PasswordInput" : "AccountInput", Color.white, 12, 24, y, 302, 56);
            image.raycastTarget = true;
            var ring = QuestionnaireUi.Ring(image.transform, "FieldBorder", new Color(.77f, .81f, .87f), 12, 1);
            QuestionnaireUi.Stretch(ring.rectTransform, 0, 0, 0, 0);
            var input = image.gameObject.AddComponent<InputField>();
            input.targetGraphic = image;
            input.transition = Selectable.Transition.None;
            // InputField and placeholder share one padded text lane. Glyphs are
            // generated at 3x resolution, like the other native mobile screens.
            input.textComponent = AuthText(image.transform, "", 16, 8, 270, 40, 17);
            input.textComponent.horizontalOverflow = HorizontalWrapMode.Overflow;
            input.textComponent.verticalOverflow = VerticalWrapMode.Truncate;
            input.placeholder = AuthText(image.transform, placeholder, 16, 8, 270, 40, 16, false, QuestionnaireUi.Muted);
            input.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Standard;
            input.lineType = InputField.LineType.SingleLine;
            input.customCaretColor = true;
            input.caretColor = QuestionnaireUi.Accent;
            input.caretWidth = 3;
            input.selectionColor = new Color(.29f, .525f, .851f, .22f);
            input.shouldHideMobileInput = false;
            var feedback = image.gameObject.AddComponent<AuthInputFocus>();
            feedback.Input = input;
            feedback.Ring = ring;
            return input;
        }

        private static Button AuthButton(Transform parent, string caption, float y,
            UnityEngine.Events.UnityAction action, bool primary = true)
        {
            var button = QuestionnaireUi.MakeButton(parent, caption, primary ? QuestionnaireUi.Primary : Color.white,
                26, 24, y, 302, 52, action);
            AuthText(button.transform, caption, 8, 0, 286, 52, primary ? 17 : 15,
                primary, primary ? Color.white : QuestionnaireUi.Accent, TextAnchor.MiddleCenter);
            return button;
        }

        private static void AuthLayout(RectTransform content, RectTransform card, Text message,
            Button primary, Button secondary, float messageTop)
        {
            var layout = content.gameObject.AddComponent<AuthMessageLayout>();
            layout.Card = card;
            layout.Message = message;
            layout.Primary = (RectTransform)primary.transform;
            layout.Secondary = (RectTransform)secondary.transform;
            layout.MessageTop = messageTop;
            layout.Apply();
        }
    }

    // Server feedback can contain a full patient preview or a long connection
    // error. Grow the card and scroll content instead of drawing over actions.
    internal sealed class AuthMessageLayout : MonoBehaviour
    {
        internal RectTransform Card, Primary, Secondary;
        internal Text Message;
        internal float MessageTop;
        private void LateUpdate() => Apply();
        internal void Apply()
        {
            if (Message == null) return;
            float height = string.IsNullOrEmpty(Message.text) ? 0 : Mathf.Max(40, Message.preferredHeight / 3f + 8);
            Message.rectTransform.sizeDelta = new Vector2(302 * 3, height * 3);
            Primary.anchoredPosition = new Vector2(24, -(MessageTop + height + 20));
            Secondary.anchoredPosition = new Vector2(24, -(MessageTop + height + 86));
            float cardHeight = MessageTop + height + 158;
            Card.sizeDelta = new Vector2(350, cardHeight);
            ((RectTransform)transform).sizeDelta = new Vector2(0, 198 + cardHeight + 28);
        }
    }

    internal sealed class AuthInputFocus : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        internal InputField Input;
        internal Image Ring;
        private void Update() => Refresh();
        private void Refresh() { if (Ring != null) Ring.color = Input != null && Input.isFocused ? QuestionnaireUi.Accent : new Color(.77f, .81f, .87f); }
        public void OnSelect(BaseEventData eventData) { if (Ring != null) Ring.color = QuestionnaireUi.Accent; }
        public void OnDeselect(BaseEventData eventData) { if (Ring != null) Ring.color = new Color(.77f, .81f, .87f); }
    }
}
