#region Using Directives

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Security.Principal;
using System.Windows.Forms;
using Fusion8.Cropper.Core;
using Fusion8.Cropper.Extensibility;

#endregion

namespace Fusion8.Cropper
{
    #region ResizeRegion enum

    internal enum ResizeRegion
    {
        None,
        N,
        NE,
        E,
        SE,
        S,
        SW,
        W,
        NW
    }

    #endregion

    /// <summary>
    ///     Represents a form for marking off the cropped area of the desktop.
    /// </summary>
    public class MainCropForm : CropForm
    {
        #region Constants

        private const int ResizeBorderWidth = 18;
        private const int TabHeight = 15;
        private const int TabTopWidth = TabHeight + 55; // 45
        private const int TabBottomWidth = TabHeight + TabTopWidth; // 60
        private const int TransparentMargin = TabBottomWidth;

        // Form measurements and sizes
        private const int MinimumDialogWidth = 230;

        private const int MinimumDialogHeight = 180;
        private const int DefaultSizingInterval = 1;
        private const int AlternateSizingInterval = 10;
        private const int MinimumThumbnailSize = 20;
        private const int MinimumSizeForCrosshairDraw = 30;
        private const int CrosshairLengthFromCenter = 10;
        private const int FormatDescriptionOffset = 5;
        private const int MinimumPadForFormatDescriptionDraw = 5;
        private const int DefaultMaxThumbnailSize = 80;
        private const int DefaultVisibleHeightWidth = 180;
        private const int DefaultPositionLeft = 100;
        private const int DefaultPositionTop = 100;
        private const double DefaultLayerOpacity = 0.4;

        #endregion
        
        #region Fields

        private bool showAbout;
        private bool showHelp;
        private bool isThumbnailed;
        private bool isDisposed;
        private bool takingScreeshot;
        private int colorIndex;

        /// <summary>
        /// Toggle to save the 'Full Sized Image' or not
        /// </summary>
        private bool saveFullImage;

        private double maxThumbSize = DefaultMaxThumbnailSize;

        // String displayed on form describing the current output format. 
        private string outputDescription;

        private Point middle;
        private Point offset;
        private Point mouseDownPoint;
        
        private Rectangle mouseDownRect;
        private Rectangle dialogCloseRectangle;
        private Rectangle thumbnailRectangle;
        private Rectangle visibleFormArea;
        private Point mouseDownLocation;

        private Size thumbnailSize = new Size(DefaultMaxThumbnailSize, DefaultMaxThumbnailSize);

        private Font feedbackFont;
        private PointF[] tabPoints;
        private float dpiScale;

        // Brushes
        // TODO: [Performance] Use one brush and set colors as needed.
        // Brush for the tab background.
        private readonly SolidBrush tabBrush = new SolidBrush(Color.SteelBlue);

        private readonly SolidBrush tabTextBrush = new SolidBrush(Color.Black);

        // Brush for the visible form background.
        private readonly SolidBrush areaBrush = new SolidBrush(Color.White);

        // Brush for the visible form background.
        private readonly Pen outlinePen = new Pen(Color.Black);

        // Brush for the drawn text and lines.
        private readonly SolidBrush formTextBrush = new SolidBrush(Color.Black);

        private readonly List<CropFormColorTable> colorTables = new List<CropFormColorTable>();
        private CropFormColorTable currentColorTable;
        private readonly ContextMenu menu = new ContextMenu();
        private MenuItem outputMenuItem;
        private MenuItem opacityMenuItem;
        private MenuItem showHideMenu;
        private MenuItem toggleThumbnailMenu;
        private NotifyIcon notifyIcon;

        private ResizeRegion resizeRegion = ResizeRegion.None;
        private ResizeRegion thumbResizeRegion;
        private readonly ImageCapture imageCapture;
        private MenuItem toggleSaveFullImage;

        #endregion
        
        #region Property Accessors

        /// <summary>
        ///     Gets the visible client rectangle.
        /// </summary>
        /// <value></value>
        private Rectangle VisibleClientRectangle
        {
            get
            {
                if (isDisposed)
                    throw new ObjectDisposedException(ToString());

                Rectangle visibleClient = new Rectangle(VisibleLeft,
                    VisibleTop,
                    VisibleWidth,
                    VisibleHeight);
                return visibleClient;
            }
        }

        #endregion

        #region Screenshot Methods

        private void TakeScreenShot(ScreenShotBounds bounds)
        {
            takingScreeshot = true;
            PaintLayeredWindow();
            try
            {
                switch (bounds)
                {
                    case ScreenShotBounds.ActiveForm:
                        imageCapture.CaptureForegroundForm();
                        break;
                    case ScreenShotBounds.Window:
                        imageCapture.CaptureWindowAtPoint(Cursor.Position);
                        break;
                    case ScreenShotBounds.FullScreen:
                        imageCapture.CaptureDesktop();
                        break;
                    case ScreenShotBounds.Rectangle:
                        if (isThumbnailed)
                            imageCapture.Capture(VisibleClientRectangle, maxThumbSize, saveFullImage);
                        else
                            imageCapture.Capture(VisibleClientRectangle);
                        break;
                }
            }
            catch (InvalidOperationException ex)
            {
                ShowError(ex.Message, "Error Taking Screenshot");
            }
            catch (FileNotFoundException)
            {
                ShowError("Cropper is unable to save the screenshot to the selected folder. This may be cause by Controlled Folder Access in Windows 10. \r\n\r\n Please add Cropper to the list of allowed apps.", "Unable to save screenshot");
            }
            finally
            {
                if (Visible && Configuration.Current.HideFormAfterCapture)
                    Hide();

                takingScreeshot = imageCapture.ContinueCapturing;
                PaintLayeredWindow();
            }
        }

        #endregion
  
        #region Private Property Accessors

        private int VisibleWidth
        {
            get => Width - TransparentMargin * 2;
            set => Width = value + TransparentMargin * 2;
        }

        private int VisibleHeight
        {
            get => Height - TransparentMargin * 2;
            set => Height = value + TransparentMargin * 2;
        }

        private int VisibleLeft
        {
            get => Left + TransparentMargin;
            set => Left = value - TransparentMargin;
        }

        private int VisibleTop
        {
            get => Top + TransparentMargin;
            set => Top = value - TransparentMargin;
        }

        private Size VisibleClientSize
        {
            get => new Size(VisibleWidth, VisibleHeight);
            set {
                VisibleWidth = value.Width;
                VisibleHeight = value.Height;
            }
        }

        #endregion

        #region .ctor

        public MainCropForm()
        {
            Configuration.Current.ActiveCropWindow = this;
            imageCapture = new ImageCapture();
            colorTables.Add(new CropFormBlueColorTable());
            colorTables.Add(new CropFormDarkColorTable());
            colorTables.Add(new CropFormLightColorTable());
            
            RegisterHotKeys();
            
            SuspendLayout();
            
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(6F, 13F);
            
            ResumeLayout(true);

            ApplyConfiguration();
            SetUpForm();
            SetUpMenu();
            SaveConfiguration();
            
            if (LimitMaxWorkingSet())
                Process.GetCurrentProcess().MaxWorkingSet = (IntPtr)5000000;
        }

        #endregion

        #region Lifecycle

        protected override void OnLoad(EventArgs e)
        {
            ScaleUI();
            SetColors();
            PaintLayeredWindow();
            
            NativeMethods.SendMessage(NativeMethods.GetTopLevelOwner(Handle), NativeMethods.WM_SETICON, NativeMethods.ICON_BIG, Icon.Handle);
            NativeMethods.SendMessage(NativeMethods.GetTopLevelOwner(Handle), NativeMethods.WM_SETICON, NativeMethods.ICON_SMALL, notifyIcon.Icon.Handle);
            NativeMethods.SendMessage(NativeMethods.GetTopLevelOwner(Handle), NativeMethods.WM_SETTEXT, 0, Text);
            base.OnLoad(e);
        }

        /// <summary>
        ///     Raises the <see cref="Form.Closing" /> event.
        /// </summary>
        /// <param name="e">A <see cref="CancelEventArgs" /> that contains the event data.</param>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (isDisposed)
                throw new ObjectDisposedException(ToString());

            SaveConfiguration();
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            NativeMethods.SendMessage(NativeMethods.GetTopLevelOwner(Handle), NativeMethods.WM_SETICON, NativeMethods.ICON_BIG, IntPtr.Zero);
            NativeMethods.SendMessage(NativeMethods.GetTopLevelOwner(Handle), NativeMethods.WM_SETICON, NativeMethods.ICON_SMALL, IntPtr.Zero);
            NativeMethods.SendMessage(NativeMethods.GetTopLevelOwner(Handle), NativeMethods.WM_SETTEXT, 0, null);
            base.OnClosed(e);
        }

        #endregion
        
        #region Hot Keys

        private void RegisterHotKeys()
        {
            const string prefix = "com.colossusinteractive.cropper.hotkey";

            HotKeys.RegisterGlobal(prefix + ".showhide", Keys.F8 | Keys.Control, this, "Show/Hide Cropper", () => CycleFormVisibility(true), groupName: "Global");

            HotKeys.RegisterLocal(prefix + ".thumbnail", Keys.T, "Toggle Thumbnail", () => ToggleThumbnail(toggleThumbnailMenu), groupName: "Thumbnail");
            HotKeys.RegisterLocal(prefix + ".increasethumb", Keys.OemOpenBrackets | Keys.Alt, "Increase Thumbnail Size", () => ResizeThumbnail(DefaultSizingInterval), groupName: "Thumbnail");
            HotKeys.RegisterLocal(prefix + ".decreasethumb", Keys.OemCloseBrackets | Keys.Alt, "Decrease Thumbnail Size", () => ResizeThumbnail(-DefaultSizingInterval), groupName: "Thumbnail");
            HotKeys.RegisterLocal(prefix + ".increasethumbalt", Keys.OemOpenBrackets | Keys.Alt | Keys.Control, "Increase Thumbnail Size Big", () => ResizeThumbnail(AlternateSizingInterval), groupName: "Thumbnail");
            HotKeys.RegisterLocal(prefix + ".decreasethumbalt", Keys.OemCloseBrackets | Keys.Alt | Keys.Control, "Decrease Thumbnail Size Big", () => ResizeThumbnail(-AlternateSizingInterval), groupName: "Thumbnail");

            HotKeys.RegisterLocal(prefix + ".centerincrease", Keys.OemOpenBrackets, "Center Size Increase", () => CenterSize(DefaultSizingInterval), groupName: "Size");
            HotKeys.RegisterLocal(prefix + ".centerdecrease", Keys.OemCloseBrackets, "Center Size Decrease", () => CenterSize(-DefaultSizingInterval), groupName: "Size");
            HotKeys.RegisterLocal(prefix + ".centerincreasealt", Keys.OemOpenBrackets | Keys.Control, "Center Size Increase Big", () => CenterSize(AlternateSizingInterval), groupName: "Size");
            HotKeys.RegisterLocal(prefix + ".centerdecreasealt", Keys.OemCloseBrackets | Keys.Control, "Center Size Decrease Big", () => CenterSize(-AlternateSizingInterval), groupName: "Size");

            HotKeys.RegisterLocal(prefix + ".increasewidth", Keys.Right | Keys.Alt, "Increase Width", () => Width = Width + DefaultSizingInterval, groupName: "Size");
            HotKeys.RegisterLocal(prefix + ".decreasewidth", Keys.Left | Keys.Alt, "Decrease Width", () => Width = Width - DefaultSizingInterval, groupName: "Size");
            HotKeys.RegisterLocal(prefix + ".increasewidthalt", Keys.Right | Keys.Alt | Keys.Control, "Increase Width Big", () => Width = Width + AlternateSizingInterval, groupName: "Size");
            HotKeys.RegisterLocal(prefix + ".decreasewidthalt", Keys.Left | Keys.Alt | Keys.Control, "Decrease Width Big", () => Width = Width - AlternateSizingInterval, groupName: "Size");
            HotKeys.RegisterLocal(prefix + ".increaseheight", Keys.Up | Keys.Alt, "Increase Height", () => Height = Height - DefaultSizingInterval, groupName: "Size");
            HotKeys.RegisterLocal(prefix + ".decreaseheight", Keys.Down | Keys.Alt, "Decrease Height", () => Height = Height + DefaultSizingInterval, groupName: "Size");
            HotKeys.RegisterLocal(prefix + ".increaseheightalt", Keys.Up | Keys.Alt | Keys.Control, "Increase Height Big", () => Height = Height - AlternateSizingInterval, groupName: "Size");
            HotKeys.RegisterLocal(prefix + ".decreaseheightalt", Keys.Down | Keys.Alt | Keys.Control, "Decrease Height Big", () => Height = Height + AlternateSizingInterval, groupName: "Size");

            HotKeys.RegisterLocal(prefix + ".moveleft", Keys.Left, "Move Left", () => Left = Left - DefaultSizingInterval, groupName: "Position");
            HotKeys.RegisterLocal(prefix + ".moveright", Keys.Right, "Move Right", () => Left = Left + DefaultSizingInterval, groupName: "Position");
            HotKeys.RegisterLocal(prefix + ".moveleftalt", Keys.Left | Keys.Control, "Move Left Big", () => Left = Left - AlternateSizingInterval, groupName: "Position");
            HotKeys.RegisterLocal(prefix + ".moverightalt", Keys.Right | Keys.Control, "Move Right Big", () => Left = Left + AlternateSizingInterval, groupName: "Position");
            HotKeys.RegisterLocal(prefix + ".moveup", Keys.Up, "Move Up", () => Top = Top - DefaultSizingInterval, groupName: "Position");
            HotKeys.RegisterLocal(prefix + ".movedown", Keys.Down, "Move Down", () => Top = Top + DefaultSizingInterval, groupName: "Position");
            HotKeys.RegisterLocal(prefix + ".moveupalt", Keys.Up | Keys.Control, "Move Up Big", () => Top = Top - AlternateSizingInterval, groupName: "Position");
            HotKeys.RegisterLocal(prefix + ".movedownalt", Keys.Down | Keys.Control, "Move Down Big", () => Top = Top + AlternateSizingInterval, groupName: "Position");

            HotKeys.RegisterLocal(prefix + ".sizedefault", Keys.NumPad0 | Keys.Control, "Default Size", () => ResetForm(true), groupName: "Size");
            HotKeys.RegisterLocal(prefix + ".size1", Keys.NumPad1 | Keys.Control, "Saved Size 1", () => ApplyCropSize(0), groupName: "Size");
            HotKeys.RegisterLocal(prefix + ".size2", Keys.NumPad2 | Keys.Control, "Saved Size 2", () => ApplyCropSize(1), groupName: "Size");
            HotKeys.RegisterLocal(prefix + ".size3", Keys.NumPad3 | Keys.Control, "Saved Size 3", () => ApplyCropSize(2), groupName: "Size");
            HotKeys.RegisterLocal(prefix + ".size4", Keys.NumPad4 | Keys.Control, "Saved Size 4", () => ApplyCropSize(3), groupName: "Size");
            HotKeys.RegisterLocal(prefix + ".size5", Keys.NumPad5 | Keys.Control, "Saved Size 5", () => ApplyCropSize(4), groupName: "Size");
            HotKeys.RegisterLocal(prefix + ".size6", Keys.NumPad6 | Keys.Control, "Saved Size 6", () => ApplyCropSize(5), groupName: "Size");
            HotKeys.RegisterLocal(prefix + ".size7", Keys.NumPad7 | Keys.Control, "Saved Size 7", () => ApplyCropSize(6), groupName: "Size");
            HotKeys.RegisterLocal(prefix + ".size8", Keys.NumPad8 | Keys.Control, "Saved Size 8", () => ApplyCropSize(7), groupName: "Size");
            HotKeys.RegisterLocal(prefix + ".size9", Keys.NumPad9 | Keys.Control, "Saved Size 9", () => ApplyCropSize(8), groupName: "Size");

            HotKeys.RegisterLocal(prefix + ".browse", Keys.B | Keys.Control, "Browse Captures", BrowseCaptures, groupName: "General");

            HotKeys.RegisterGlobal(prefix + ".printscreen", Keys.PrintScreen, this, "Capture Desktop", () => TakeScreenShot(ScreenShotBounds.FullScreen), true);
            HotKeys.RegisterGlobal(prefix + ".altprintscreen", Keys.PrintScreen | Keys.Alt, this, "Capture Active Window", () => TakeScreenShot(ScreenShotBounds.ActiveForm), true);
            HotKeys.RegisterGlobal(prefix + ".ctrlaltprintscreen", Keys.PrintScreen | Keys.Alt | Keys.Control, this, "Capture at Point", () => TakeScreenShot(ScreenShotBounds.Window), true);

            HotKeys.RegisterLocal(prefix + ".nextcolor", Keys.Tab, "Next Color", CycleColors, true);
            HotKeys.RegisterLocal(prefix + ".nextsize", Keys.Tab | Keys.Shift, "Next Size", CycleSizes, true);
            HotKeys.RegisterLocal(prefix + ".nextcenteredsize", Keys.Tab | Keys.Shift | Keys.Control, "Next Centered Size", CycleSizesCentered, true);
            HotKeys.RegisterLocal(prefix + ".nextthumbsize", Keys.Tab | Keys.Control, "Next Thumb Size", CycleThumbSizes, true);

            HotKeys.RegisterLocal(prefix + ".hide", Keys.Escape, "Hide Cropper", () => CycleFormVisibility(true), true, "General");
            HotKeys.RegisterLocal(prefix + ".screenshot", Keys.Enter, "Take Screenshot", () => TakeScreenShot(ScreenShotBounds.Rectangle), true, "General");
        }

        /// <summary>
        ///     Raises the <see cref="CropForm.HotKeyPressed" /> event.
        /// </summary>
        /// <param name="e">A <see cref="KeyEventArgs" /> that contains the event data.</param>
        protected override void OnHotKeyPressed(KeyEventArgs e)
        {
            if (isDisposed)
                throw new ObjectDisposedException(ToString());

            base.OnHotKeyPressed(e);

            if (!e.Handled)
                HotKeys.Process(e.KeyData);
        }
        
        #endregion
        
        #region Menu Handling

        /// <summary>
        ///     Setup the main context menu
        /// </summary>
        private void SetUpMenu()
        {
            AddTopLevelMenuItems();
            if (Configuration.Current.ShowOpacityMenu)
                AddOpacitySubMenu();
            AddOutputSubMenus();
            CheckThumbnailToggleState();
        }

        private void AddTopLevelMenuItems()
        {
            outputMenuItem = AddTopLevelMenuItem(SR.MenuOutput, null);
            toggleThumbnailMenu = AddTopLevelMenuItem(SR.MenuThumbnail, HandleMenuThumbnailClick);
            toggleThumbnailMenu.Checked = isThumbnailed;
            toggleSaveFullImage = AddTopLevelMenuItem(SR.MenuSaveFullImage, HandleMenuSaveFullImageClick);
            toggleSaveFullImage.Checked = saveFullImage;
            AddTopLevelMenuItem(SR.MenuOptions, HandleMenuOptionsClick);
            AddTopLevelMenuItem(SR.MenuBrowse, HandleMenuBrowseClick);
            AddTopLevelMenuItem(SR.MenuSeperator, null);
            AddTopLevelMenuItem(SR.MenuOnTop, HandleMenuOnTopClick).Checked = TopMost;
            AddTopLevelMenuItem(SR.MenuInvert, HandleMenuInvertClick);

            MenuItem predefinedSizes = AddTopLevelMenuItem(SR.MenuSize, null);
            if (Configuration.Current.PredefinedSizes.Length == 0)
            {
                MenuItem nextSize = AddSubMenuItem(predefinedSizes, "None Defined", null);
                nextSize.Enabled = false;
            }
            foreach (CropSize size in Configuration.Current.PredefinedSizes)
            {
                MenuItem nextSize = AddSubMenuItem(predefinedSizes, size.ToString(), HandleMenuSizeClick);
                nextSize.Tag = size;
            }
            AddSubMenuItem(predefinedSizes, SR.MenuSeperator, null);
            AddSubMenuItem(predefinedSizes, "Add Current", HandleMenuSizeCurrentClick);

            MenuItem predefinedThumbSizes = AddTopLevelMenuItem(SR.MenuThumbSize, null);
            if (Configuration.Current.PredefinedThumbSizes.Length == 0)
            {
                MenuItem nextSize = AddSubMenuItem(predefinedThumbSizes, "None Defined", null);
                nextSize.Enabled = false;
            }
            foreach (double size in Configuration.Current.PredefinedThumbSizes)
            {
                MenuItem nextSize = AddSubMenuItem(predefinedThumbSizes, size.ToString(), HandleMenuThumbSizeClick);
                nextSize.Tag = size;
            }
            AddSubMenuItem(predefinedThumbSizes, SR.MenuSeperator, null);
            AddSubMenuItem(predefinedThumbSizes, "Add Current", HandleMenuThumbSizeCurrentClick);

            if (Configuration.Current.ShowOpacityMenu)
                opacityMenuItem = AddTopLevelMenuItem(SR.MenuOpacity, null);

            AddTopLevelMenuItem(SR.MenuSeperator, null);
            showHideMenu = AddTopLevelMenuItem(SR.MenuHide, HandleMenuShowHideClick);
            AddTopLevelMenuItem(SR.MenuReset, HandleMenuResetClick);
            AddTopLevelMenuItem(SR.MenuSeperator, null);
            MenuItem helpMenuItem = AddTopLevelMenuItem(SR.MenuHelp, null);
            AddSubMenuItem(helpMenuItem, SR.MenuHelpHowTo, HandleMenuHelpClick);
            AddSubMenuItem(helpMenuItem, SR.MenuAbout, HandleMenuAboutClick);
            AddSubMenuItem(helpMenuItem, SR.MenuSeperator, null);
            AddSubMenuItem(helpMenuItem, SR.MenuHelpWeb, HandleMenuHelpWebClick);
            AddTopLevelMenuItem(SR.MenuSeperator, null);
            AddTopLevelMenuItem(SR.MenuExit, HandleMenuExitClick);
        }

        private void AddOpacitySubMenu()
        {
            for (int i = 10; i <= 90; i += 10)
            {
                MenuItem subMenu = new MenuItem(i + "%") {RadioCheck = true};
                subMenu.Click += HandleMenuOpacityClick;
                opacityMenuItem.MenuItems.Add(subMenu);
                if (i == Convert.ToInt32(Configuration.Current.UserOpacity * 100))
                    subMenu.Checked = true;
            }
        }

        private MenuItem AddTopLevelMenuItem(string text, EventHandler handler)
        {
            MenuItem mi = new MenuItem(text);
            if (handler != null)
                mi.Click += handler;
            menu.MenuItems.Add(mi);

            return mi;
        }

        private static MenuItem AddSubMenuItem(Menu parent, string text, EventHandler handler)
        {
            MenuItem mi = new MenuItem(text);
            if (handler != null)
                mi.Click += handler;
            parent.MenuItems.Add(mi);

            return mi;
        }

        private void RefreshMenuItems()
        {
            menu.MenuItems.Clear();
            SetUpMenu();
        }

        private void AddOutputSubMenus()
        {
            foreach (IPersistableImageFormat imageOutputFormat in ImageCapture.ImageOutputs)
            {
                MenuItem menuItem = imageOutputFormat.Menu;
                imageOutputFormat.ImageFormatClick += HandleImageFormatClick;
                if (menuItem != null)
                {
                    outputMenuItem.MenuItems.Add(menuItem);
                    if (imageCapture.ImageFormat == null || menuItem.Text != imageCapture.ImageFormat.Description)
                        ClearImageFormatChecks(menuItem);
                    else if (!menuItem.IsParent)
                        menuItem.Checked = true;
                }
            }
        }

        private static void ClearImageFormatChecks(Menu menuItem)
        {
            foreach (MenuItem item in menuItem.MenuItems)
            {
                if (item.IsParent) ClearImageFormatChecks(item);
                item.Checked = false;
            }
        }

        private void ResetForm(bool sizeOnly = false)
        {
            VisibleWidth = DefaultVisibleHeightWidth;
            VisibleHeight = DefaultVisibleHeightWidth;
            if (sizeOnly)
                return;

            VisibleLeft = DefaultPositionLeft;
            VisibleTop = DefaultPositionTop;
            Configuration.Current.UserOpacity = DefaultLayerOpacity;
            maxThumbSize = DefaultMaxThumbnailSize;
        }

        private void HandleMenuOnTopClick(object sender, EventArgs e)
        {
            MenuItem mi = (MenuItem) sender;
            TopMost = mi.Checked = !mi.Checked;
        }

        private void HandleMenuShowHideClick(object sender, EventArgs e)
        {
            CycleFormVisibility(true);
        }

        private void CycleFormVisibility(bool allowHide)
        {
            if (Visible && allowHide)
            {
                DialogCloseIfNeeded();
                showHideMenu.Text = SR.MenuShow;
                Hide();
                if (LimitMaxWorkingSet()) Process.GetCurrentProcess().MaxWorkingSet = (IntPtr) 5000000;
                GC.Collect(2);
            }
            else if (!Visible)
            {
                showHideMenu.Text = SR.MenuHide;
                Show();
                Activate();
            }
            else
            {
                Activate();
            }
        }

        private void HandleMenuResetClick(object sender, EventArgs e)
        {
            ResetForm();
        }

        private void HandleMenuThumbnailClick(object sender, EventArgs e)
        {
            ToggleThumbnail((MenuItem) sender);
        }

        private void HandleMenuSaveFullImageClick(object sender, EventArgs e)
        {
            ToggleSaveFullImage((MenuItem)sender);
        }

        private void HandleMenuInvertClick(object sender, EventArgs e)
        {
            CycleColors();
        }

        private void HandleMenuSizeClick(object sender, EventArgs e)
        {
            if (!(sender is MenuItem item))
                return;

            if (!(item.Tag is CropSize))
                return;

            CropSize size = (CropSize) item.Tag;
            VisibleClientSize = new Size(size.Width, size.Height);
        }

        private void HandleMenuThumbSizeClick(object sender, EventArgs e)
        {
            MenuItem item = sender as MenuItem;

            if (!(item?.Tag is double))
                return;

            double size = (double)item.Tag;

            maxThumbSize = size;
            PaintLayeredWindow();
        }

        private void HandleMenuSizeCurrentClick(object sender, EventArgs e)
        {
            CropSize size = new CropSize(VisibleClientSize.Width, VisibleClientSize.Height);
            List<CropSize> list = new List<CropSize>(Configuration.Current.PredefinedSizes);
            if (list.Contains(size))
                return;

            list.Add(size);
            CropSize[] cropSizes = list.ToArray();

            //Array.Sort(cropSizes);

            Configuration.Current.PredefinedSizes = cropSizes;
            RefreshMenuItems();
        }

        private void HandleMenuThumbSizeCurrentClick(object sender, EventArgs e)
        {
            double size = maxThumbSize;
            List<double> list = new List<double>(Configuration.Current.PredefinedThumbSizes);
            if (list.Contains(size))
                return;

            list.Add(size);
            double[] cropSizes = list.ToArray();

            //Array.Sort(cropSizes);

            Configuration.Current.PredefinedThumbSizes = cropSizes;
            RefreshMenuItems();
        }
        //

        private void HandleMenuOptionsClick(object sender, EventArgs e)
        {
            ShowOptionsDialog();
        }

        private void ShowOptionsDialog()
        {
            Options options = new Options();
            if (options.ShowDialog(this) != DialogResult.OK)
                return;

            SetColors();
            RefreshMenuItems();
            SaveConfiguration();
        }

        private void HandleMenuBrowseClick(object sender, EventArgs e)
        {
            BrowseCaptures();
        }

        private void BrowseCaptures()
        {
            if (!Directory.Exists(Configuration.Current.OutputPath))
                Directory.CreateDirectory(Configuration.Current.OutputPath);
            if (string.IsNullOrEmpty(imageCapture.LastImageCaptured) || !File.Exists(imageCapture.LastImageCaptured))
            {
                Process.Start(Configuration.Current.OutputPath);
            }
            else
            {
                // Browse to folder and select last image
                // Thanks to Jon Galloway
                ProcessStartInfo processStartInfo = new ProcessStartInfo
                {
                    FileName = "explorer",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal,
                    Arguments = "/e,/select,\"" + imageCapture.LastImageCaptured + "\""
                };
                Process.Start(processStartInfo);
            }
        }

        private void HandleMenuHelpClick(object sender, EventArgs e)
        {
            DialogShow(b => showHelp = b);
        }

        private void HandleMenuAboutClick(object sender, EventArgs e)
        {
            DialogShow(b => showAbout = b);
        }

        private static void HandleMenuHelpWebClick(object sender, EventArgs e)
        {
            Process.Start(SR.HomepageLinkUrl);
        }

        private void HandleMenuExitClick(object sender, EventArgs e)
        {
            Close();
        }

        private void EnsureMinimumDialogWidth()
        {
            if ((VisibleWidth < MinimumDialogWidth) | (VisibleHeight < MinimumDialogHeight))
                VisibleClientSize = new Size(MinimumDialogWidth, MinimumDialogHeight);
        }

        private void HandleMenuOpacityClick(object sender, EventArgs e)
        {
            MenuItem menuItem = (MenuItem) sender;
            foreach (MenuItem childItems in menuItem.Parent.MenuItems)
                childItems.Checked = false;
            menuItem.Checked = true;

            Configuration.Current.UserOpacity = double.Parse(menuItem.Text.Replace("%", ""), CultureInfo.InvariantCulture) / 100;
            SetColors();
        }

        private void HandleImageFormatClick(object sender, ImageFormatEventArgs e)
        {
            ClearImageFormatChecks(outputMenuItem);
            e.ClickedMenuItem.Checked = true;
            imageCapture.ImageFormat = e.ImageOutputFormat;
            outputDescription = e.ImageOutputFormat.Description;
            notifyIcon.Text = "Cropper\nOutput: " + outputDescription;
            PaintLayeredWindow();
        }

        #endregion

        #region Mouse Overrides

        /// <summary>
        ///     Raises the <see cref="E:System.Windows.Forms.Control.MouseDown" /> event.
        /// </summary>
        /// <param name="e">A <see cref="T:System.Windows.Forms.MouseEventArgs" /> that contains the event data.</param>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            HandleMouseDown(e);
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            CheckForDialogClosing();
            HandleMouseUp();
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e.Delta > 0)
                CenterSize(AlternateSizingInterval);
            else
                CenterSize(-AlternateSizingInterval);
        }

        /// <summary>
        ///     Raises the <see cref="E:System.Windows.Forms.Control.DoubleClick" /> event.
        /// </summary>
        /// <param name="e">An <see cref="T:System.EventArgs" /> that contains the event data.</param>
        protected override void OnDoubleClick(EventArgs e)
        {
            TakeScreenShot(ScreenShotBounds.Rectangle);
        }

        /// <summary>
        ///     Raises the <see cref="E:System.Windows.Forms.Control.MouseMove" /> event.
        /// </summary>
        /// <param name="e">A <see cref="T:System.Windows.Forms.MouseEventArgs" /> that contains the event data.</param>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            HandleMouseMove(e);
        }

        #endregion

        #region Event Overrides
        
        /// <summary>
        ///     Raises the <see cref="Form.KeyDown" /> event.
        /// </summary>
        /// <param name="e">A <see cref="KeyEventArgs" /> that contains the event data.</param>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (isDisposed)
                throw new ObjectDisposedException(ToString());

            base.OnKeyDown(e);
            if (e.Handled)
                return;

            e.Handled = true;
            e.SuppressKeyPress = true;
            HotKeys.Process(e.KeyData);
        }

        protected override void OnResize(EventArgs e)
        {
            middle.X = VisibleWidth / 2 + TransparentMargin;
            middle.Y = VisibleHeight / 2 + TransparentMargin;

            if (VisibleWidth <= 1)
                VisibleWidth = 1;
            if (VisibleHeight <= 1)
                VisibleHeight = 1;

            visibleFormArea = new Rectangle(TransparentMargin,
                TransparentMargin,
                VisibleWidth - 1,
                VisibleHeight - 1);

            if (showAbout || showHelp)
            {
                showAbout = false;
                showHelp = false;
            }

            base.OnResize(e);
        }

        #endregion

        #region Form Manipulation

        private void HandleMouseUp()
        {
            resizeRegion = ResizeRegion.None;
            thumbResizeRegion = ResizeRegion.None;
            Cursor = Cursors.Default;
        }

        private void HandleMouseDown(MouseEventArgs e)
        {
            offset = new Point(e.X, e.Y);
            mouseDownRect = ClientRectangle;
            mouseDownPoint = MousePosition;
            mouseDownLocation = Location;

            if (IsInResizeArea())
                resizeRegion = GetResizeRegion();
            else if (IsInThumbnailResizeArea())
                thumbResizeRegion = ResizeRegion.SE;
        }

//		private void ShowMoveCursor(MouseEventArgs e)
//		{
//			if (resizeRegion == ResizeRegion.None &&
//				thumbResizeRegion == ResizeRegion.None &&
//				e.Button == MouseButtons.Left)
//				Cursor = Cursors.SizeAll;
//		}

        private void HandleMouseMove(MouseEventArgs e)
        {
            bool mouseIsInResizeArea = resizeRegion != ResizeRegion.None;
            bool mouseIsInThumbResizeArea = thumbResizeRegion != ResizeRegion.None;

            if (mouseIsInResizeArea && (ModifierKeys & Keys.Shift) == Keys.Shift && (ModifierKeys & Keys.Control) == Keys.Control)
            {
                HandleSquarePropCenterResize();
            }
            else if(mouseIsInResizeArea && (ModifierKeys & Keys.Control) == Keys.Control)
            {
                HandleCenterResize();
            }
            else if(mouseIsInResizeArea && (ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                HandleSquarePropResize();
            }
            else if (mouseIsInResizeArea)
            {
                HandleResize();
            }
            else if (mouseIsInThumbResizeArea)
            {
                HandleThumbResize();
            }
            else
            {
                bool mouseOnFormShouldMove = e.Button == MouseButtons.Left;
                bool mouseInResizeAreaCanResize = IsInResizeArea() && e.Button != MouseButtons.Left;
                bool mouseInThumbResizeAreaCanResize = IsInThumbnailResizeArea() && e.Button != MouseButtons.Left;
                bool mouseNotInResizeArea = resizeRegion == ResizeRegion.None;
                bool mouseNotInThumbResizeArea = thumbResizeRegion == ResizeRegion.None;

                if (mouseOnFormShouldMove)
                    Location = CalculateNewFormLocation();

                if (mouseInResizeAreaCanResize)
                    SetResizeCursor(GetResizeRegion());
                else if (mouseInThumbResizeAreaCanResize)
                    SetResizeCursor(ResizeRegion.SE);
                else if (mouseNotInResizeArea && mouseNotInThumbResizeArea && !mouseOnFormShouldMove)
                    Cursor = Cursors.Default;
            }
        }

        private void ResizeThumbnail(int interval)
        {
            maxThumbSize = maxThumbSize + interval;
            if (maxThumbSize < MinimumThumbnailSize)
                maxThumbSize = MinimumThumbnailSize;
            PaintLayeredWindow();
        }

        private ResizeRegion GetResizeRegion()
        {
            Point clientCursorPos = PointToClient(MousePosition);
            if (
                (clientCursorPos.X >= Width - (TransparentMargin + ResizeBorderWidth)) &
                (clientCursorPos.Y >= Height - (TransparentMargin + ResizeBorderWidth)))
                return ResizeRegion.SE;
            if (clientCursorPos.X >= Width - (TransparentMargin + ResizeBorderWidth))
                return ResizeRegion.E;
            if (clientCursorPos.Y >= Height - (TransparentMargin + ResizeBorderWidth))
                return ResizeRegion.S;
            return ResizeRegion.None;
        }

        private void HandleResize()
        {
            int diffX = MousePosition.X - mouseDownPoint.X;
            int diffY = MousePosition.Y - mouseDownPoint.Y;

            FreezePainting = true;
            switch (resizeRegion)
            {
                case ResizeRegion.E:
                    Width = mouseDownRect.Width + diffX;
                    break;
                case ResizeRegion.S:
                    Height = mouseDownRect.Height + diffY;
                    break;
                case ResizeRegion.SE:
                    Width = mouseDownRect.Width + diffX;
                    Height = mouseDownRect.Height + diffY;
                    break;
            }
            FreezePainting = false;
        }
        /// <summary>
        /// 1:1 aspect ratio resizing (Shift+LMB)
        /// </summary>
        private void HandleSquarePropResize()
        {
            int diffX = MousePosition.X - mouseDownPoint.X;
            int diffY = MousePosition.Y - mouseDownPoint.Y;

            FreezePainting = true;
            switch (resizeRegion)
            {
                case ResizeRegion.E:
                    Width = mouseDownRect.Width + diffX;
                    Height = Width;
                    break;
                case ResizeRegion.S:
                    Height = mouseDownRect.Height + diffY;
                    Width = Height;
                    break;
                case ResizeRegion.SE:
                    Width = mouseDownRect.Width + diffX;
                    Height = Width;
                    break;
            }
            FreezePainting = false;
        }
        /// <summary>
        /// Resizing relative to the center (Ctrl+LMB)
        /// </summary>
        private void HandleCenterResize()
        {
            int diffX = MousePosition.X - mouseDownPoint.X;
            int diffY = MousePosition.Y - mouseDownPoint.Y;

            FreezePainting = true;
            switch (resizeRegion)
            {
                case ResizeRegion.E:
                    Left = mouseDownLocation.X - diffX;
                    Top = mouseDownLocation.Y;
                    Width = mouseDownRect.Width + (2 * diffX);
                    break;
                case ResizeRegion.S:
                    Left = mouseDownLocation.X;
                    Top = mouseDownLocation.Y - diffY;
                    Height = mouseDownRect.Height + (2 * diffY);
                    break;
                case ResizeRegion.SE:
                    Left = mouseDownLocation.X - diffX;
                    Top = mouseDownLocation.Y - diffY;
                    Width = mouseDownRect.Width + (2 * diffX);
                    Height = mouseDownRect.Height + (2 * diffY);
                    break;
            }
            FreezePainting = false;
        }
        /// <summary>
        /// Resizing relative to the center with 1:1 aspect ratio (Ctrl+Shift+LMB)
        /// </summary>
        private void HandleSquarePropCenterResize()
        {
            int diffX = MousePosition.X - mouseDownPoint.X;
            int diffY = MousePosition.Y - mouseDownPoint.Y;

            FreezePainting = true;
            switch (resizeRegion)
            {
                case ResizeRegion.E:
                    Left = mouseDownLocation.X - diffX;
                    Top = mouseDownLocation.Y - diffX;
                    Width = mouseDownRect.Width + (2 * diffX);
                    Height = Width;
                    break;
                case ResizeRegion.S:
                    Left = mouseDownLocation.X - diffY;
                    Top = mouseDownLocation.Y - diffY;
                    Height = mouseDownRect.Height + (2 * diffY);
                    Width = Height;
                    break;
                case ResizeRegion.SE:
                    Left = mouseDownLocation.X - diffX;
                    Top = mouseDownLocation.Y - diffX;
                    Width = mouseDownRect.Width + (2 * diffX);
                    Height = Width;

                    break;
            }
            FreezePainting = false;
        }

        private void HandleThumbResize()
        {
            int diffX = MousePosition.X - mouseDownPoint.X;
            int diffY = MousePosition.Y - mouseDownPoint.Y;

            mouseDownPoint.X = MousePosition.X;
            mouseDownPoint.Y = MousePosition.Y;

            ResizeThumbnail(diffX + diffY);
        }

        private void CenterSize(int interval)
        {
            if (interval < -AlternateSizingInterval || interval > AlternateSizingInterval)
                throw new ArgumentOutOfRangeException(nameof(interval), interval, SR.ExceptionCenterSizeOutOfRange);

            if ((VisibleWidth > interval) & (VisibleHeight > interval))
            {
                int interval2 = interval * 2;
                Width = Width - interval2;
                Left = Left + interval;
                Height = Height - interval2;
                Top = Top + interval;
            }
        }

        private void CycleColors()
        {
            if (colorIndex >= colorTables.Count - 1)
                colorIndex = 0;
            else
                colorIndex++;
            SetColors();
            PaintLayeredWindow();
        }

        private void CycleSizes()
        {
            Size size = Configuration.Current.NextFormSize();
            if (size != Size.Empty)
                VisibleClientSize = size;
        }

        private void CycleSizesCentered()
        {
            Size size = Configuration.Current.NextFormSize();
            if (size != Size.Empty)
            {
                Left = Left - (size.Width - VisibleWidth) / 2;
                Top = Top - (size.Height - VisibleHeight) / 2;
                VisibleClientSize = size;
            }
        }

        private void CycleThumbSizes()
        {
            double size = Configuration.Current.NextFormThumbSize();
            if (size != 0.0)
                maxThumbSize = size;
            PaintLayeredWindow();
        }

        private void SetColors()
        {
            currentColorTable = colorTables[colorIndex];
            int areaAlpha = (int) (Configuration.Current.UserOpacity * 255);

            if (Configuration.Current.UsePerPixelAlpha)
            {
                LayerOpacity = 1.0;
                currentColorTable.MainAlphaChannel = areaAlpha;
            }
            else
            {
                LayerOpacity = Configuration.Current.UserOpacity;
                currentColorTable.MainAlphaChannel = 255;
            }

            tabBrush.Color = currentColorTable.TabColor;
            areaBrush.Color = currentColorTable.FormColor;
            formTextBrush.Color = currentColorTable.FormTextColor;
            tabTextBrush.Color = currentColorTable.TabTextColor;
            outlinePen.Color = currentColorTable.LineColor;
        }

        private bool IsInResizeArea()
        {
            Point clientCursorPos = PointToClient(MousePosition);

            Rectangle clientVisibleRect = ClientRectangle;
            clientVisibleRect.Inflate(-TransparentMargin, -TransparentMargin);

            Rectangle resizeInnerRect = clientVisibleRect;
            resizeInnerRect.Inflate(-ResizeBorderWidth, -ResizeBorderWidth);

            return clientVisibleRect.Contains(clientCursorPos) && !resizeInnerRect.Contains(clientCursorPos);
        }

        private bool IsInThumbnailResizeArea()
        {
            Point clientCursorPos = PointToClient(MousePosition);

            Rectangle resizeInnerRect = new Rectangle(thumbnailRectangle.Right - 15,
                thumbnailRectangle.Bottom - 15,
                15, 15);

            return resizeInnerRect.Contains(clientCursorPos);
        }

        private void ToggleThumbnail(MenuItem mi)
        {
            isThumbnailed = mi.Checked = !mi.Checked;
            CheckThumbnailToggleState();
            PaintLayeredWindow();
        }

        private void ToggleSaveFullImage(MenuItem mi)
        {
            saveFullImage = mi.Checked = !mi.Checked;
            PaintLayeredWindow();
        }

        private void CheckThumbnailToggleState()
        {
            if (!toggleThumbnailMenu.Checked)
            {
                toggleSaveFullImage.Checked = true;
                saveFullImage = true;
                toggleSaveFullImage.Enabled = false;
            }
            else
            {
                toggleSaveFullImage.Checked = saveFullImage;
                toggleSaveFullImage.Enabled = true;
            }
        }

        private void SetResizeCursor(ResizeRegion region)
        {
            switch (region)
            {
                case ResizeRegion.S:
                    Cursor = Cursors.SizeNS;
                    break;
                case ResizeRegion.E:
                    Cursor = Cursors.SizeWE;
                    break;
                case ResizeRegion.SE:
                    Cursor = Cursors.SizeNWSE;
                    break;
                default:
                    Cursor = Cursors.Default;
                    break;
            }
        }

        private bool IsMouseInRectangle(Rectangle rectangle)
        {
            Point clientCursorPos = PointToClient(MousePosition);
            return rectangle.Contains(clientCursorPos);
        }

        private Point CalculateNewFormLocation()
        {
            return new Point(MousePosition.X - offset.X, MousePosition.Y - offset.Y);
        }

        #endregion

        #region Helper Methods

        private void ApplyCropSize(int index)
        {
            CropSize[] predefinedSizes = Configuration.Current.PredefinedSizes;
            if (predefinedSizes.Length <= index)
                return;

            CropSize size = predefinedSizes[index];
            VisibleClientSize = new Size(size.Width, size.Height);
        }

        private void CheckForDialogClosing()
        {
            if (IsMouseInRectangle(dialogCloseRectangle))
                DialogClose();
        }

        private void DialogClose()
        {
            dialogCloseRectangle.Inflate(-dialogCloseRectangle.Size.Width, -dialogCloseRectangle.Size.Height);
            showAbout = false;
            showHelp = false;
            PaintLayeredWindow();
        }

        private void DialogCloseIfNeeded()
        {
            if (showHelp || showAbout)
                DialogClose();
        }

        private void DialogShow(Action<bool> setTheFlag)
        {
            DialogCloseIfNeeded();
            EnsureMinimumDialogWidth();
            setTheFlag(true);
            PaintLayeredWindow();
            CycleFormVisibility(false);
        }
        
        private void SetUpForm()
        {
            ResourceManager resources = new ResourceManager(typeof(MainCropForm));

            notifyIcon = new NotifyIcon
            {
                Icon = (Icon) resources.GetObject("NotifyIcon"),
                Visible = true
            };
            notifyIcon.MouseUp += HandleNotifyIconMouseUp;
            notifyIcon.Text = "Cropper\nOutput: " + outputDescription;

            Text = "Cropper";
            Icon = (Icon) resources.GetObject("Icon");

            ContextMenu = menu;
            notifyIcon.ContextMenu = menu;
        }

        private static void ShowError(string text, string caption)
        {
            ShowMessage(text, caption, MessageBoxIcon.Error);
        }

        private static void ShowMessage(string text, string caption, MessageBoxIcon icon)
        {
            MessageBox.Show(text, caption, MessageBoxButtons.OK, icon);
        }

        /// <summary>
        ///     Determines if the MaxWorkingSet should be limited.
        /// </summary>
        /// <returns>true if MaxWorkingSet should be limited; otherwise, false</returns>
        /// <remarks>
        ///     This is only used to prevent an exception in Windows 2000 when the user is not part of the
        ///     BUILTIN\Administrators group. This can be removed when Windows 2000 is no longer supported (July 13, 2010)
        /// </remarks>
        private static bool LimitMaxWorkingSet()
        {
            bool windows2000 = Environment.OSVersion.Version.Major == 5 &&
                               Environment.OSVersion.Version.Minor == 0 &&
                               Environment.OSVersion.Version.Build == 2195;
            if (windows2000)
            {
                string administratorsGroupSid = "S-1-5-32-544";
                foreach (IdentityReference group in WindowsIdentity.GetCurrent().Groups)
                    if (group.Value == administratorsGroupSid)
                        return true;

                return false;
            }

            return true;
        }

        #endregion

        #region Painting

        protected override void OnPaintLayer(PaintLayerEventArgs e)
        {
            Graphics graphics = e.Graphics;

            PaintUI(graphics);
            base.OnPaintLayer(e);
        }

        private void PaintUI(Graphics graphics)
        {
            if (currentColorTable == null) 
                return;
            
            if (takingScreeshot)
            {
                outlinePen.Color = Color.FromArgb(areaBrush.Color.A, currentColorTable.LineHighlightColor);
                outlinePen.Width = TabHeight / 2f;

                graphics.DrawRectangle(outlinePen, Rectangle.Inflate(visibleFormArea, TabHeight / 4 + 1, TabHeight / 4 + 1));
            }
            else
            {
                outlinePen.Color = currentColorTable.LineColor;
                outlinePen.Width = 1f;

                PaintMainFormArea(graphics, visibleFormArea);
                if (showHelp)
                {
                    DrawHelp(graphics);
                }
                else if (showAbout)
                {
                    DrawAbout(graphics);
                }
                else
                {
                    PaintThumbnailIndicator(graphics, VisibleWidth, VisibleHeight);
                    PaintCrosshairs(graphics, VisibleWidth, VisibleHeight);
                }
                Point grabberCorner = new Point(Width - TransparentMargin, Height - TransparentMargin);
                PaintGrabber(graphics, grabberCorner);
                PaintOutputFormat(graphics, VisibleWidth, VisibleHeight);
            }

            PaintSizeTabs(graphics);
        }

        private void PaintGrabber(Graphics graphics, Point grabberStart)
        {
            int yOffset = grabberStart.Y - 4;
            int xOffset = grabberStart.X - 4;
            graphics.DrawLine(outlinePen, grabberStart.X - 5, yOffset, xOffset, grabberStart.Y - 5);
            graphics.DrawLine(outlinePen, grabberStart.X - 10, yOffset, xOffset, grabberStart.Y - 10);
            graphics.DrawLine(outlinePen, grabberStart.X - 15, yOffset, xOffset, grabberStart.Y - 15);
        }

        private void PaintSizeTabs(Graphics graphics)
        {
            graphics.FillPolygon(tabBrush, tabPoints);
            
            PaintWidthString(graphics);
            PaintHeightString(graphics);
        }

        private void PaintHeightString(Graphics graphics)
        {
            graphics.RotateTransform(90);
            graphics.DrawString(
                $"{VisibleHeight}{(dpiScale != 1f ? " (" + Math.Round(VisibleHeight / dpiScale) + ")" : "")} px",
                feedbackFont,
                tabTextBrush,
                TransparentMargin,
                -TransparentMargin);
        }

        private void PaintWidthString(Graphics graphics)
        {
            graphics.DrawString(
                $"{VisibleWidth}{(dpiScale != 1f ? " (" + Math.Round(VisibleWidth / dpiScale) + ")" : "")} px",
                feedbackFont,
                tabTextBrush,
                TransparentMargin,
                TransparentMargin - 14 * dpiScale);
        }

        private void PaintMainFormArea(Graphics graphics, Rectangle cropArea)
        {
            graphics.FillRectangle(areaBrush, cropArea);
            graphics.DrawRectangle(outlinePen, cropArea);
        }

        private void PaintThumbnailIndicator(Graphics graphics, int paintWidth, int paintHeight)
        {
            if (isThumbnailed)
            {
                double thumbRatio;
                if (paintHeight > paintWidth)
                    thumbRatio = paintHeight / maxThumbSize;
                else
                    thumbRatio = paintWidth / maxThumbSize;
                
                thumbnailSize.Width = Convert.ToInt32(paintWidth / thumbRatio);
                thumbnailSize.Height = Convert.ToInt32(paintHeight / thumbRatio);

//                thumbnailSize.Width = (int)Math.Round(Convert.ToInt32(paintWidth / thumbRatio) * dpiScale, MidpointRounding.ToEven);
//                thumbnailSize.Height = (int)Math.Round(Convert.ToInt32(paintHeight / thumbRatio) * dpiScale, MidpointRounding.ToEven);

                if (paintWidth > thumbnailSize.Width + 50 && paintHeight > thumbnailSize.Height + 30)
                {
                    string size = thumbnailSize.Width + "x" + thumbnailSize.Height;
                    string max = maxThumbSize + " px max";
                    SizeF dimensionSize = graphics.MeasureString(size, feedbackFont);
                    SizeF maxSize = graphics.MeasureString(max, feedbackFont);

                    graphics.DrawString(
                        max,
                        feedbackFont,
                        formTextBrush,
                        middle.X - maxSize.Width / 2,
                        middle.Y - thumbnailSize.Height / 2 - maxSize.Height);

                    graphics.DrawString(
                        size,
                        feedbackFont,
                        formTextBrush,
                        middle.X - dimensionSize.Width / 2,
                        middle.Y + thumbnailSize.Height / 2);

                    thumbnailRectangle = new Rectangle(
                        middle.X - thumbnailSize.Width / 2,
                        middle.Y - thumbnailSize.Height / 2,
                        thumbnailSize.Width,
                        thumbnailSize.Height);

                    graphics.DrawRectangle(
                        outlinePen, thumbnailRectangle);

                    if (thumbnailRectangle.Height > 22)
                    {
                        Point grabberCorner = new Point(thumbnailRectangle.Right, thumbnailRectangle.Bottom);
                        PaintGrabber(graphics, grabberCorner);
                    }
                }
            }
        }

        private void PaintOutputFormat(Graphics graphics, int paintWidth, int paintHeight)
        {
            SizeF formatSize = graphics.MeasureString(outputDescription, feedbackFont);
            if (formatSize.Width + MinimumPadForFormatDescriptionDraw < paintWidth &&
                formatSize.Height + MinimumPadForFormatDescriptionDraw < paintHeight)
                graphics.DrawString(
                    outputDescription,
                    feedbackFont,
                    formTextBrush,
                    TransparentMargin + FormatDescriptionOffset,
                    TransparentMargin + FormatDescriptionOffset);
        }

        private void PaintCrosshairs(Graphics graphics, int paintWidth, int paintHeight)
        {
            if ((paintWidth > MinimumSizeForCrosshairDraw) & (paintHeight > MinimumSizeForCrosshairDraw))
            {
                graphics.DrawLine(
                    outlinePen,
                    middle.X,
                    middle.Y + CrosshairLengthFromCenter,
                    middle.X,
                    middle.Y - CrosshairLengthFromCenter);

                graphics.DrawLine(
                    outlinePen,
                    middle.X + CrosshairLengthFromCenter,
                    middle.Y,
                    middle.X - CrosshairLengthFromCenter,
                    middle.Y);
            }
        }

        private void DrawAbout(Graphics g)
        {
            DrawDialog(g, "About Cropper v" + Application.ProductVersion, SR.MessageAbout);
        }

        private void DrawHelp(Graphics g)
        {
            DrawDialog(g, "Help", SR.MessageHelp);
        }

        private void DrawDialog(Graphics g, string title, string text)
        {
            StringFormat format = new StringFormat(StringFormatFlags.NoClip)
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center
            };

            Rectangle aboutRectangle = new Rectangle(Width / 2 - 90, Height / 2 - 60, MinimumDialogHeight, 120);

            // Box
            //
            g.FillRectangle(Brushes.SteelBlue, aboutRectangle);
            g.DrawRectangle(Pens.Black, aboutRectangle);

            //Contents
            //
            aboutRectangle.Inflate(-5, -5);
            aboutRectangle.Y = aboutRectangle.Y + 5;

            Font textFont = new Font("Arial", 8f);
            //Draw text
            //
            g.DrawString(text, textFont, Brushes.White, aboutRectangle, format);

            //Title
            //
            aboutRectangle.Inflate(5, -47);
            aboutRectangle.Y = Height / 2 - 60;
            g.FillRectangle(Brushes.Black, aboutRectangle);
            g.DrawRectangle(Pens.Black, aboutRectangle);

            aboutRectangle.Inflate(-5, 0);
            g.DrawString(title, textFont, Brushes.White, aboutRectangle, format);

            //Close
            //
            aboutRectangle.Inflate(-78, 0);
            aboutRectangle.X = Width / 2 + 76;
            g.FillRectangle(Brushes.Red, aboutRectangle);
            g.DrawRectangle(Pens.Black, aboutRectangle);

            StringFormat closeFormat = new StringFormat(StringFormatFlags.NoClip | StringFormatFlags.DirectionVertical);
            format.Alignment = StringAlignment.Near;
            format.LineAlignment = StringAlignment.Center;

            Font closeFont = new Font("Verdana", 10.5f, FontStyle.Bold);
            aboutRectangle.Inflate(3, -1);
            g.DrawString("X",
                closeFont,
                Brushes.White,
                aboutRectangle,
                closeFormat);

            dialogCloseRectangle = aboutRectangle;
            format.Dispose();
            closeFormat.Dispose();
            textFont.Dispose();
            closeFont.Dispose();
        }

        #endregion

        #region DPI

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            
            ScaleUI();
            PaintLayeredWindow();
        }


        private void ScaleUI()
        {
            dpiScale = DeviceDpi / 96.0f;

            feedbackFont = new Font("Verdana", 8f);
            
            tabPoints = new[]
            {
                new PointF(TransparentMargin - TabHeight * dpiScale,
                    TransparentMargin - TabHeight * dpiScale),
                new PointF(TransparentMargin + TabTopWidth * dpiScale,
                    TransparentMargin - TabHeight * dpiScale),
                new PointF(TransparentMargin + TabBottomWidth * dpiScale,
                    TransparentMargin),
                new PointF(TransparentMargin,
                    TransparentMargin),
                new PointF(TransparentMargin,
                    TransparentMargin + TabBottomWidth * dpiScale),
                new PointF(TransparentMargin - TabHeight * dpiScale,
                    TransparentMargin + TabTopWidth * dpiScale)
            };
        }

        #endregion

        #region Configuration

        private void ApplyConfiguration()
        {
            Settings settings = Configuration.Current;
            Location = settings.Location;
            TopMost = settings.AlwaysOnTop;
            
            VisibleClientSize = settings.UserSize;
            colorIndex = settings.ColorIndex;
            isThumbnailed = settings.IsThumbnailed;
            maxThumbSize = settings.MaxThumbnailSize;

            saveFullImage = settings.SaveFullImage;

            if (settings.HotKeySettings != null)
                foreach (HotKeySetting hk in settings.HotKeySettings)
                {
                    if (string.IsNullOrEmpty(hk.Id))
                        continue;

                    HotKeys.UpdateHotKey(hk.Id, (Keys)hk.KeyCode);
                }

            if (settings.UserOpacity < 0.1 || settings.UserOpacity > 0.9)
                settings.UserOpacity = 0.4;

            LayerOpacity = !settings.UsePerPixelAlpha ? settings.UserOpacity : 1.0;

            if (ImageCapture.ImageOutputs[settings.ImageFormat] != null)
            {
                imageCapture.ImageFormat = ImageCapture.ImageOutputs[settings.ImageFormat];
                outputDescription = imageCapture.ImageFormat.Description;
            }
            else
            {
                outputDescription = SR.MessageNoOutputLoaded;
            }
        }

        private void SaveConfiguration()
        {
            string description = string.Empty;
            if (imageCapture.ImageFormat != null)
                description = imageCapture.ImageFormat.Description;

            Configuration.Current.ImageFormat = description;
            Configuration.Current.MaxThumbnailSize = maxThumbSize;
            Configuration.Current.IsThumbnailed = isThumbnailed;
            Configuration.Current.Location = Location;
            Configuration.Current.UserSize = VisibleClientSize;
            Configuration.Current.ColorIndex = colorIndex;
            Configuration.Current.AlwaysOnTop = TopMost;
            Configuration.Current.Hidden = !Visible;
            Configuration.Current.SaveFullImage = saveFullImage;

            try
            {
                Configuration.Save();
            }
            catch (Exception)
            {
                MessageBox.Show(
                    "There was a problem saving your current settings. This may be caused by a plug-in. No changes have been made to your configuration.",
                    "Configuration Error", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
            }
        }

        #endregion
        
        #region Tray Icon

        /// <summary>
        ///     Handles the MouseUp event of the NotifyIcon control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.Forms.MouseEventArgs" /> instance containing the event data.</param>
        private void HandleNotifyIconMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                CycleFormVisibility(false);
        }

        #endregion

        #region IDisposable Implementation

        protected override void Dispose(bool disposing)
        {
            isDisposed = true;
            if (disposing)
            {
                feedbackFont?.Dispose();
                menu?.Dispose();
                tabBrush?.Dispose();
                areaBrush?.Dispose();
                formTextBrush?.Dispose();
                notifyIcon?.Dispose();
                outlinePen?.Dispose();
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}