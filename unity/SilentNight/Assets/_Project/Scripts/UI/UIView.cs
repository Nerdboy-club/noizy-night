using System;
using UnityEngine.UIElements;

namespace SilentNight.UI
{
    /// <summary>
    /// Base for all screens. Each view builds its own VisualElement tree in C# (no UXML required)
    /// and styles it with USS classes from Game.uss. Owned and driven by UIRoot.
    /// </summary>
    public abstract class UIView
    {
        public VisualElement Root { get; private set; }
        public bool IsVisible { get; private set; }
        protected UIRoot Owner { get; private set; }

        /// <summary>Element that gets the short "pop" scale-in when shown (usually the dialog plaque).</summary>
        protected virtual VisualElement PopTarget => null;

        internal void Build(UIRoot owner, VisualElement parent)
        {
            Owner = owner;
            Root = CreateRoot();
            Root.style.display = DisplayStyle.None;
            parent.Add(Root);
        }

        protected abstract VisualElement CreateRoot();

        /// <summary>Subscribe to GameEvents. Called by UIRoot.OnEnable.</summary>
        public virtual void Bind() { }
        /// <summary>Unsubscribe from GameEvents. Called by UIRoot.OnDisable.</summary>
        public virtual void Unbind() { }

        public void SetVisible(bool visible)
        {
            if (visible) Show(); else Hide();
        }

        public void Show()
        {
            if (Root == null) return;
            bool wasVisible = IsVisible;
            IsVisible = true;
            Root.style.display = DisplayStyle.Flex;
            if (!wasVisible)
            {
                var pop = PopTarget;
                if (pop != null)
                {
                    // Start slightly shrunk, then let the USS transition scale it to 1.
                    // If the scheduler never fires the panel is merely at 92% scale — still usable.
                    pop.AddToClassList("sn-pop");
                    pop.schedule.Execute(() => pop.RemoveFromClassList("sn-pop")).StartingIn(30);
                }
            }
            OnShow();
        }

        public void Hide()
        {
            if (Root == null || !IsVisible) return;
            IsVisible = false;
            Root.style.display = DisplayStyle.None;
            OnHide();
        }

        protected virtual void OnShow() { }
        protected virtual void OnHide() { }

        /// <summary>Per-frame update while visible (unscaled delta time). Must not allocate.</summary>
        public virtual void Tick(float unscaledDt) { }
    }

    /// <summary>Tiny element factory so view code stays short.</summary>
    public static class UIFactory
    {
        public static VisualElement El(string classes, VisualElement parent = null)
        {
            var e = new VisualElement();
            AddClasses(e, classes);
            parent?.Add(e);
            return e;
        }

        public static VisualElement Ignored(string classes, VisualElement parent = null)
        {
            var e = El(classes, parent);
            e.pickingMode = PickingMode.Ignore;
            return e;
        }

        public static Label Text(string text, string classes, VisualElement parent = null)
        {
            var l = new Label(text);
            l.pickingMode = PickingMode.Ignore;
            AddClasses(l, classes);
            parent?.Add(l);
            return l;
        }

        public static Button Btn(string text, Action onClick, string classes, VisualElement parent = null)
        {
            var b = new Button(onClick) { text = text ?? string.Empty };
            // Drop the default theme look so our chunky style is the only one applied.
            b.RemoveFromClassList(Button.ussClassName);
            b.focusable = false; // avoid hidden buttons reacting to Space/Enter navigation
            AddClasses(b, "sn-btn " + classes);
            parent?.Add(b);
            return b;
        }

        public static IconElement Icon(IconKind kind, string classes, VisualElement parent = null)
        {
            var i = new IconElement(kind);
            AddClasses(i, classes);
            parent?.Add(i);
            return i;
        }

        /// <summary>Keyboard key cap, e.g. [E].</summary>
        public static Label Key(string key, VisualElement parent = null) => Text(key, "sn-key", parent);

        public static void AddClasses(VisualElement e, string classes)
        {
            if (string.IsNullOrEmpty(classes)) return;
            foreach (var c in classes.Split(' '))
                if (c.Length > 0) e.AddToClassList(c);
        }
    }
}
