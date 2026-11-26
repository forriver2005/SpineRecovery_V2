using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpineRecovery.App
{
    public sealed partial class AppShellUi
    {
        private GameObject CreateLoginPanel(Transform parent)
        {
            // The supplied artwork uses a 390 x 844 frame. Keep the image and
            // all hit targets in one coordinate system inside the safe area.
            RectTransform root = QuestionnaireUi.CreateScreen(parent, "LoginPanel", null, out GameObject panel);
            panel.GetComponent<Image>().color = new Color32(245, 249, 255, 255);
            Vector2 At(float x, float y) => new Vector2(x - 195f, 422f - y);
            CreateRasterImage(root, "AppUi/LoginCover", Vector2.zero, new Vector2(390, 844));

            accountInput = CreateInput(root, "请输入手机号或邮箱", At(195, 377), false);
            passwordInput = CreateInput(root, "请输入密码", At(195, 469), true);
            foreach (var input in new[] { accountInput, passwordInput })
            {
                input.GetComponent<RectTransform>().sizeDelta = new Vector2(306, 50);
                MakeOverlayInput(input);
                input.textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
                input.textComponent.rectTransform.offsetMax = new Vector2(-40, -4);
            }
            var eye = CreateButton(root, "显示或隐藏密码", At(323, 469), () => {
                passwordInput.inputType = passwordInput.inputType == InputField.InputType.Password
                    ? InputField.InputType.Standard : InputField.InputType.Password;
                passwordInput.ForceLabelUpdate();
            }, 44, 44);
            MakeOverlayButton(eye);
            MakeOverlayButton(CreateButton(root, "立即注册", At(307, 521),
                () => bootstrap.Router.Navigate(AppRoute.Register), 82, 44));
            MakeOverlayButton(CreateButton(root, "登录", At(195, 576), HandleLoginClicked, 306, 52));
            loginMessage = CreateLabel(root, "", At(148, 521), 12);
            loginMessage.rectTransform.sizeDelta = new Vector2(208, 48);
            loginMessage.raycastTarget = false;
            return panel;
        }

        private static void CreateRasterImage(Transform parent, string resourcePath, Vector2 position, Vector2 size)
        {
            Texture2D texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null) return;
            GameObject imageObject = new GameObject("Raster:" + resourcePath, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Image image = imageObject.GetComponent<Image>();
            image.sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f);
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.raycastTarget = false;
        }

        private static void MakeOverlayInput(InputField input)
        {
            if (input == null) return;
            Image image = input.GetComponent<Image>();
            if (image != null)
            {
                // This InputField is only an interaction layer over the
                // approved phone PNG. Keep its Image transparent and detach
                // it from Selectable's targetGraphic so Unity cannot tint it
                // white on focus and cover the authored icon/border.
                image.color = new Color(1f, 1f, 1f, 0f);
                image.raycastTarget = true;
            }
            input.targetGraphic = null;
            input.transition = Selectable.Transition.None;
            RoundedBorderGraphic border = input.GetComponentInChildren<RoundedBorderGraphic>(true);
            if (border != null)
            {
                border.SetBorderColor(new Color(1f, 1f, 1f, 0f));
                border.raycastTarget = false;
            }
            if (input.textComponent != null)
            {
                input.textComponent.color = new Color(0.25f, 0.30f, 0.38f, 1f);
                // The login design root is scaled up for the portrait canvas;
                // generate the glyphs at a larger size so typed characters do
                // not become soft after the transform scale.
                // The login artwork is authored at 390 design pixels (the
                // source PNG is now imported at 2x). Keep live glyphs at the
                // same visual size as the artwork instead of the old 30px
                // value, which rendered typed text oversized and soft.
                input.textComponent.font = AppBootstrap.Instance != null && AppBootstrap.Instance.uiFont != null
                    ? AppBootstrap.Instance.uiFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                input.textComponent.fontSize = 17;
                input.textComponent.alignByGeometry = false;
                input.textComponent.horizontalOverflow = HorizontalWrapMode.Overflow;
                input.textComponent.verticalOverflow = VerticalWrapMode.Truncate;
            }
            if (input.placeholder is Text placeholder)
                placeholder.color = new Color(1f, 1f, 1f, 0f);
        }

        private static void MakeOverlayButton(Button button)
        {
            if (button == null) return;
            Image image = button.GetComponent<Image>();
            if (image != null) image.color = new Color(1f, 1f, 1f, 0f);
            Text label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = new Color(1f, 1f, 1f, 0f);
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0f);
            colors.selectedColor = new Color(1f, 1f, 1f, 0f);
            button.colors = colors;
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
