﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform;
using JetBrains.Annotations;

namespace Avalonia.Controls
{
    /// <summary>
    /// Base class for top-level windows.
    /// </summary>
    /// <remarks>
    /// This class acts as a base for top level windows such as <see cref="Window"/> and
    /// <see cref="PopupRoot"/>. It handles scheduling layout, styling and rendering as well as
    /// tracking the window <see cref="TopLevel.ClientSize"/> and <see cref="IsActive"/> state.
    /// </remarks>
    public class WindowBase : TopLevel
    {
        /// <summary>
        /// Defines the <see cref="IsActive"/> property.
        /// </summary>
        public static readonly DirectProperty<WindowBase, bool> IsActiveProperty =
            AvaloniaProperty.RegisterDirect<WindowBase, bool>(nameof(IsActive), o => o.IsActive);

        private bool _isActive;
        private bool _ignoreVisibilityChange;

        static WindowBase()
        {
            IsVisibleProperty.OverrideDefaultValue<WindowBase>(false);
            IsVisibleProperty.Changed.AddClassHandler<WindowBase>(x => x.IsVisibleChanged);
        }

        public WindowBase(IWindowBaseImpl impl) : this(impl, AvaloniaLocator.Current)
        {
        }

        public WindowBase(IWindowBaseImpl impl, IAvaloniaDependencyResolver dependencyResolver) : base(impl, dependencyResolver)
        {
            impl.Activated = HandleActivated;
            impl.Deactivated = HandleDeactivated;
            impl.PositionChanged = HandlePositionChanged;
            this.GetObservable(ClientSizeProperty).Skip(1).Subscribe(x => PlatformImpl?.Resize(x));
        }

        /// <summary>
        /// Fired when the window is activated.
        /// </summary>
        public event EventHandler Activated;

        /// <summary>
        /// Fired when the window is deactivated.
        /// </summary>
        public event EventHandler Deactivated;

        /// <summary>
        /// Fired when the window position is changed.
        /// </summary>
        public event EventHandler<PointEventArgs> PositionChanged;

        [CanBeNull]
        public new IWindowBaseImpl PlatformImpl => (IWindowBaseImpl) base.PlatformImpl;

        /// <summary>
        /// Gets a value that indicates whether the window is active.
        /// </summary>
        public bool IsActive
        {
            get { return _isActive; }
            private set { SetAndRaise(IsActiveProperty, ref _isActive, value); }
        }

        /// <summary>
        /// Gets or sets the window position in screen coordinates.
        /// </summary>
        public Point Position
        {
            get { return PlatformImpl?.Position ?? default(Point); }
            set
            {
                if (PlatformImpl is IWindowBaseImpl impl)
                    impl.Position = value;
            }
        }

        /// <summary>
        /// Whether an auto-size operation is in progress.
        /// </summary>
        protected bool AutoSizing
        {
            get;
            private set;
        }

        /// <summary>
        /// Activates the window.
        /// </summary>
        public void Activate()
        {
            PlatformImpl?.Activate();
        }

        /// <summary>
        /// Hides the popup.
        /// </summary>
        public virtual void Hide()
        {
            _ignoreVisibilityChange = true;

            try
            {
                PlatformImpl?.Hide();
                IsVisible = false;
            }
            finally
            {
                _ignoreVisibilityChange = false;
            }
        }

        /// <summary>
        /// Shows the popup.
        /// </summary>
        public virtual void Show()
        {
            _ignoreVisibilityChange = true;

            try
            {
                EnsureInitialized();
                IsVisible = true;
                LayoutManager.Instance.ExecuteInitialLayoutPass(this);
                PlatformImpl?.Show();
            }
            finally
            {
                _ignoreVisibilityChange = false;
            }
        }

        /// <summary>
        /// Begins an auto-resize operation.
        /// </summary>
        /// <returns>A disposable used to finish the operation.</returns>
        /// <remarks>
        /// When an auto-resize operation is in progress any resize events received will not be
        /// cause the new size to be written to the <see cref="Layoutable.Width"/> and
        /// <see cref="Layoutable.Height"/> properties.
        /// </remarks>
        protected IDisposable BeginAutoSizing()
        {
            AutoSizing = true;
            return Disposable.Create(() => AutoSizing = false);
        }

        /// <summary>
        /// Carries out the arrange pass of the window.
        /// </summary>
        /// <param name="finalSize">The final window size.</param>
        /// <returns>The <paramref name="finalSize"/> parameter unchanged.</returns>
        protected override Size ArrangeOverride(Size finalSize)
        {
            using (BeginAutoSizing())
            {
                PlatformImpl?.Resize(finalSize);
            }

            return base.ArrangeOverride(PlatformImpl?.ClientSize ?? default(Size));
        }

        /// <summary>
        /// Ensures that the window is initialized.
        /// </summary>
        protected void EnsureInitialized()
        {
            if (!IsInitialized)
            {
                var init = (ISupportInitialize)this;
                init.BeginInit();
                init.EndInit();
            }
        }

        protected override void HandleClosed()
        {
            _ignoreVisibilityChange = true;

            try
            {
                IsVisible = false;
                base.HandleClosed();
            }
            finally
            {
                _ignoreVisibilityChange = false;
            }
        }

        /// <summary>
        /// Handles a resize notification from <see cref="ITopLevelImpl.Resized"/>.
        /// </summary>
        /// <param name="clientSize">The new client size.</param>
        protected override void HandleResized(Size clientSize)
        {
            if (!AutoSizing)
            {
                Width = clientSize.Width;
                Height = clientSize.Height;
            }
            ClientSize = clientSize;
            LayoutManager.Instance.ExecuteLayoutPass();
            Renderer?.Resized(clientSize);
        }

        /// <summary>
        /// Handles a window position change notification from 
        /// <see cref="IWindowBaseImpl.PositionChanged"/>.
        /// </summary>
        /// <param name="pos">The window position.</param>
        private void HandlePositionChanged(Point pos)
        {
            PositionChanged?.Invoke(this, new PointEventArgs(pos));
        }

        /// <summary>
        /// Handles an activated notification from <see cref="IWindowBaseImpl.Activated"/>.
        /// </summary>
        private void HandleActivated()
        {
            Activated?.Invoke(this, EventArgs.Empty);

            var scope = this as IFocusScope;

            if (scope != null)
            {
                FocusManager.Instance.SetFocusScope(scope);
            }

            IsActive = true;
        }

        /// <summary>
        /// Handles a deactivated notification from <see cref="IWindowBaseImpl.Deactivated"/>.
        /// </summary>
        private void HandleDeactivated()
        {
            IsActive = false;

            Deactivated?.Invoke(this, EventArgs.Empty);
        }

        private void IsVisibleChanged(AvaloniaPropertyChangedEventArgs e)
        {
            if (!_ignoreVisibilityChange)
            {
                if ((bool)e.NewValue)
                {
                    Show();
                }
                else
                {
                    Hide();
                }
            }
        }

        /// <summary>
        /// Starts moving a window with left button being held. Should be called from left mouse button press event handler
        /// </summary>
        public void BeginMoveDrag() => PlatformImpl?.BeginMoveDrag();

        /// <summary>
        /// Starts resizing a window. This function is used if an application has window resizing controls. 
        /// Should be called from left mouse button press event handler
        /// </summary>
        public void BeginResizeDrag(WindowEdge edge) => PlatformImpl?.BeginResizeDrag(edge);
    }
}
