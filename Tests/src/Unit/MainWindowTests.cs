namespace BlueHeighliner.Bundler.Tests;

public sealed class MainWindowTests
{
    private readonly Mock<IMainViewModel> model = new();
    private readonly Mock<IDialogs> dialogs = new();

    private MainWindow Show()
    {
        BundleRowViewModel row = new BundleRowViewModel(new BundleStatus { Name = "git", Description = "Git", Target = "Ubuntu 24.04", Requires = [], State = BundleStanding.Available, FileModified = new DateTime(2026, 10, 30, 9, 19, 0) }, [new ServiceRow("git", "2.56.0", "Source control", ["GPL-2.0", "MIT"], "Clear.")], false);
        model.SetupGet(m => m.Bundles).Returns([row]);
        model.SetupGet(m => m.Current).Returns(row);
        model.SetupGet(m => m.HasCurrent).Returns(true);
        model.SetupGet(m => m.Log).Returns([]);
        model.SetupGet(m => m.Notice).Returns(string.Empty);
        model.Setup(m => m.Start()).Returns(Task.CompletedTask);
        MainWindow window = new(model.Object, dialogs.Object);
        window.Show();
        return window;
    }

    private static Button FindButton(MainWindow window, string name)
        => window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);

    [AvaloniaFact]
    public void Window_ShowsTheBundleRows()
    {
        MainWindow window = Show();
        window.UpdateLayout();

        List<string?> texts = window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList();
        Assert.Contains("git", texts);
        Assert.Contains("available", texts);
        Assert.Contains("Target", texts);
        Assert.Contains("Ubuntu 24.04", texts);
        model.Verify(m => m.Start(), Times.Once);
    }

    [AvaloniaFact]
    public void WhileWorking_OnlyCancelIsEnabledAndTheStatusBarIsBlue()
    {
        model.SetupGet(m => m.IsBusy).Returns(true);
        model.SetupGet(m => m.CanBuild).Returns(false);
        model.SetupGet(m => m.CanInstall).Returns(false);
        model.SetupGet(m => m.CanRefresh).Returns(false);
        model.SetupGet(m => m.Activity).Returns("Building");
        MainWindow window = Show();
        window.UpdateLayout();

        string[] enabled = window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyEnabled).Select(button => button.Name ?? string.Empty).Where(name => name is not ("Minimize" or "Maximize" or "Restore" or "Close")).ToArray();
        Border status = window.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("statusbar"));

        Assert.Equal(["Cancel"], enabled);
        Assert.Contains("working", status.Classes);
    }

    [AvaloniaFact]
    public void WhenReady_TheStatusBarIsGray()
    {
        model.SetupGet(m => m.Activity).Returns("Ready");
        MainWindow window = Show();

        Border status = window.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("statusbar"));

        Assert.DoesNotContain("working", status.Classes);
    }

    [AvaloniaFact]
    public void LicenseLinks_AreListedAndOpenTheLicense()
    {
        model.Setup(m => m.ShowLicense(It.IsAny<LicenseLink>())).Returns(Task.CompletedTask);
        MainWindow window = Show();
        window.UpdateLayout();

        Button[] links = window.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("link")).ToArray();
        links[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(["GPL-2.0", "MIT"], links.Select(link => link.Content));
        model.Verify(m => m.ShowLicense(It.Is<LicenseLink>(link => link.Id == "MIT" && link.All.Count == 2 && link.Service == "git")), Times.Once);
    }

    [AvaloniaFact]
    public void LicenseWindow_HasATabForEachLicenseAndShowsTheSelectedOne()
    {
        LicenseWindow window = new("git", [("GPL-2.0", "gpl text"), ("MIT", "mit text")], "MIT");
        window.Show();

        TabControl tabs = window.FindControl<TabControl>("Tabs")!;

        Assert.Equal(["GPL-2.0", "MIT"], tabs.Items.OfType<TabItem>().Select(tab => tab.Header));
        Assert.Equal(1, tabs.SelectedIndex);
        Assert.Equal("mit text", Assert.IsType<TextBox>(((TabItem)tabs.Items[1]!).Content).Text);
        Assert.Contains("git", window.Title);
    }

    [AvaloniaFact]
    public void Window_HasNoTitleBarAndItsOwnWindowControls()
    {
        MainWindow window = Show();

        Assert.Equal(WindowDecorations.BorderOnly, window.WindowDecorations);
        Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
        Assert.Equal(["Minimize", "Maximize", "Restore", "Close"], window.GetVisualDescendants().OfType<WindowControlsView>().Single().GetVisualDescendants().OfType<Button>().Select(button => button.Name));
    }

    [AvaloniaFact]
    public void WindowControls_MinimizeMaximizeRestoreAndClose()
    {
        MainWindow window = Show();
        WindowControlsView controls = window.GetVisualDescendants().OfType<WindowControlsView>().Single();
        Button Find(string name) => controls.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);

        Find("Maximize").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(WindowState.Maximized, window.WindowState);
        Assert.False(Find("Maximize").IsVisible);
        Assert.True(Find("Restore").IsVisible);

        Find("Restore").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(WindowState.Normal, window.WindowState);

        Find("Minimize").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(WindowState.Minimized, window.WindowState);

        window.WindowState = WindowState.Normal;
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        Find("Close").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(closed);
    }

    [AvaloniaTheory]
    [InlineData("Build")]
    [InlineData("Install")]
    [InlineData("Uninstall")]
    [InlineData("Reinstall")]
    [InlineData("Refresh")]
    public void ActionButtons_CallTheViewModel(string name)
    {
        MainWindow window = Show();
        model.Setup(m => m.Build()).Returns(Task.CompletedTask);
        model.Setup(m => m.Install()).Returns(Task.CompletedTask);
        model.Setup(m => m.Uninstall()).Returns(Task.CompletedTask);
        model.Setup(m => m.Reinstall()).Returns(Task.CompletedTask);
        model.Setup(m => m.Refresh()).Returns(Task.CompletedTask);
        model.SetupGet(m => m.CanBuild).Returns(true);

        FindButton(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        model.Verify(m => m.Build(), Times.Exactly(name == "Build" ? 1 : 0));
        model.Verify(m => m.Install(), Times.Exactly(name == "Install" ? 1 : 0));
        model.Verify(m => m.Uninstall(), Times.Exactly(name == "Uninstall" ? 1 : 0));
        model.Verify(m => m.Reinstall(), Times.Exactly(name == "Reinstall" ? 1 : 0));
        model.Verify(m => m.Refresh(), Times.Exactly(name == "Refresh" ? 1 : 0));
    }

    [AvaloniaFact]
    public void SelectAndCancelButtons_CallTheViewModel()
    {
        MainWindow window = Show();

        FindButton(window, "SelectAll").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        FindButton(window, "SelectNone").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        FindButton(window, "Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        FindButton(window, "ClearLog").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        model.Verify(m => m.SelectAll(true), Times.Once);
        model.Verify(m => m.SelectAll(false), Times.Once);
        model.Verify(m => m.Cancel(), Times.Once);
        model.Verify(m => m.ClearLog(), Times.Once);
    }

    [AvaloniaFact]
    public void Window_TakesOverTheDialogs()
    {
        MainWindow window = Show();

        dialogs.VerifySet(d => d.Owner = window, Times.Once);
    }

    [AvaloniaFact]
    public void ConfirmDialog_YesAndNoCloseWithTheAnswer()
    {
        ConfirmDialog yes = new() { Question = "Install git?" };
        ConfirmDialog no = new() { Question = "Install git?" };
        bool? yesAnswer = null;
        bool? noAnswer = null;
        yes.Closed += (_, _) => yesAnswer ??= false;
        no.Closed += (_, _) => noAnswer ??= false;
        yes.Show();
        no.Show();

        Assert.Equal("Install git?", yes.FindControl<TextBlock>("Message")!.Text);
        yes.FindControl<Button>("Yes")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        no.FindControl<Button>("No")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.False(yes.IsVisible);
        Assert.False(no.IsVisible);
    }

    [AvaloniaFact]
    public async Task Dialogs_WithoutAnOwner_Agree()
        => Assert.True(await new Dialogs(Mock.Of<ILicenseTexts>()).Confirm("sure?"));
}
