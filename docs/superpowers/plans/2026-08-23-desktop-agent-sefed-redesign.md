# Desktop Agent Visual Redesign (Sefed Brand) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restyle the desktop agent's single window using the same Sefed brand tokens as the portal redesign, resized for touch-friendly kiosk use.

**Architecture:** A merged `ResourceDictionary` (`Styles.xaml`) holds brush/font resources and implicit `Button`/`TextBox` styles; `MainWindow.xaml` is restyled in place with no binding/command changes. Fonts and the logo PNGs are embedded as WPF `Resource`-build-action files.

**Tech Stack:** .NET 8 WPF, XAML styles/templates (no new NuGet packages).

## Global Constraints

- Exact brush values (same as the portal spec, copy verbatim): `SurfaceBrush #FFFFFF`, `SurfaceMutedBrush #F4F6F6`, `InkBrush #174249`, `InkSoftBrush #3C6066`, `BorderColorBrush #DADADA`, `AccentBrush #E2B260`, `AccentHoverBrush #D0822F`, `AccentInkBrush #174249`, `DangerBrush #DC2626`.
- No change to any `Command`, `Binding`, or code-behind logic — XAML/resource files only. `MainViewModel.cs` is not touched by this plan.
- Window becomes `Height="480" Width="560"` (up from `320x420` — note the goal is a LARGER window, both dimensions increase).
- "Sefed Attendance" replaces "ZAK Attendance" in the `Window.Title` (the only remaining occurrence of the old name after the portal redesign already covered the portal's own instance).
- `TextBox`'s custom `ControlTemplate` MUST include a `ScrollViewer` named exactly `PART_ContentHost` — WPF's text-editing behavior is wired to that exact name; omitting it silently breaks the ability to type into the box.

---

### Task 1: Font/icon assets and the brand `ResourceDictionary`

**Files:**
- Create: `agent/src/AttendanceAgent/Fonts/Figtree-Regular.ttf`, `Figtree-Medium.ttf`, `Figtree-SemiBold.ttf`, `Figtree-Bold.ttf`, `ChakraPetch-Regular.ttf`, `ChakraPetch-SemiBold.ttf` (copy from `portal/public/fonts/` — already verified in the portal work that these 6 files, all living in one folder, expose exactly two font families, "Figtree" and "Chakra Petch", regardless of which specific weight file WPF's font resolver happens to read)
- Create: `agent/src/AttendanceAgent/Assets/sefed-icon.png`, `sefed-icon-dark.png` (copy from `portal/public/sefed-icon.png`/`sefed-icon-dark.png` — same files already used in the portal)
- Create: `agent/src/AttendanceAgent/Styles.xaml`
- Modify: `agent/src/AttendanceAgent/AttendanceAgent.csproj`
- Modify: `agent/src/AttendanceAgent/App.xaml`

**Interfaces:**
- Produces: WPF resources `SurfaceBrush`, `SurfaceMutedBrush`, `InkBrush`, `InkSoftBrush`, `BorderColorBrush`, `AccentBrush`, `AccentHoverBrush`, `AccentInkBrush`, `DangerBrush`, `FigtreeFont`, `ChakraPetchFont`, and the named style `PrimaryButtonStyle` — all consumed by Task 2's `MainWindow.xaml`. Implicit (un-keyed) `Button`/`TextBox` styles apply automatically to every button/textbox in the app once this dictionary is merged into `App.xaml`.

- [ ] **Step 1: Copy the font and icon assets**

```bash
mkdir -p "agent/src/AttendanceAgent/Fonts" "agent/src/AttendanceAgent/Assets"
cp portal/public/fonts/Figtree-Regular.ttf agent/src/AttendanceAgent/Fonts/
cp portal/public/fonts/Figtree-Medium.ttf agent/src/AttendanceAgent/Fonts/
cp portal/public/fonts/Figtree-SemiBold.ttf agent/src/AttendanceAgent/Fonts/
cp portal/public/fonts/Figtree-Bold.ttf agent/src/AttendanceAgent/Fonts/
cp portal/public/fonts/ChakraPetch-Regular.ttf agent/src/AttendanceAgent/Fonts/
cp portal/public/fonts/ChakraPetch-SemiBold.ttf agent/src/AttendanceAgent/Fonts/
cp portal/public/sefed-icon.png agent/src/AttendanceAgent/Assets/
cp portal/public/sefed-icon-dark.png agent/src/AttendanceAgent/Assets/
```

- [ ] **Step 2: Register the assets as WPF `Resource`-build-action items**

```xml
<!-- In agent/src/AttendanceAgent/AttendanceAgent.csproj, add a new ItemGroup (anywhere after the existing PropertyGroup): -->
  <ItemGroup>
    <Resource Include="Fonts\*.ttf" />
    <Resource Include="Assets\*.png" />
  </ItemGroup>
```

- [ ] **Step 3: Create the brand resource dictionary**

```xml
<!-- agent/src/AttendanceAgent/Styles.xaml — full file -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Brand colors — same hex values as the portal redesign's design tokens.
         Named BorderColorBrush (not BorderBrush) to avoid any confusion with
         Control.BorderBrush, WPF's own built-in dependency property. -->
    <SolidColorBrush x:Key="SurfaceBrush" Color="#FFFFFF" />
    <SolidColorBrush x:Key="SurfaceMutedBrush" Color="#F4F6F6" />
    <SolidColorBrush x:Key="InkBrush" Color="#174249" />
    <SolidColorBrush x:Key="InkSoftBrush" Color="#3C6066" />
    <SolidColorBrush x:Key="BorderColorBrush" Color="#DADADA" />
    <SolidColorBrush x:Key="AccentBrush" Color="#E2B260" />
    <SolidColorBrush x:Key="AccentHoverBrush" Color="#D0822F" />
    <SolidColorBrush x:Key="AccentInkBrush" Color="#174249" />
    <SolidColorBrush x:Key="DangerBrush" Color="#DC2626" />

    <!-- Fonts embedded via Resource build action (Step 2) — pack URI + #FamilyName
         resolves to whichever family is present in the Fonts folder, regardless of
         which specific weight file WPF happens to read from disk first (verified
         against the actual .ttf files during the portal redesign: all weight
         variants of one typeface share the same family name). -->
    <FontFamily x:Key="FigtreeFont">pack://application:,,,/Fonts/#Figtree</FontFamily>
    <FontFamily x:Key="ChakraPetchFont">pack://application:,,,/Fonts/#Chakra Petch</FontFamily>

    <!-- Default TextBox style — WPF's TextBox has no native CornerRadius, so the
         default ControlTemplate is replaced. PART_ContentHost is REQUIRED to be
         named exactly that for text editing to work at all. -->
    <Style TargetType="TextBox">
        <Setter Property="BorderBrush" Value="{StaticResource BorderColorBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Padding" Value="8,6" />
        <Setter Property="FontSize" Value="16" />
        <Setter Property="FontFamily" Value="{StaticResource FigtreeFont}" />
        <Setter Property="Foreground" Value="{StaticResource InkBrush}" />
        <Setter Property="Background" Value="{StaticResource SurfaceBrush}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="TextBox">
                    <Border Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="6">
                        <ScrollViewer x:Name="PART_ContentHost"
                                      Margin="{TemplateBinding Padding}"
                                      VerticalAlignment="Center" />
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Style.Triggers>
            <Trigger Property="IsFocused" Value="True">
                <Setter Property="BorderBrush" Value="{StaticResource AccentBrush}" />
                <Setter Property="BorderThickness" Value="2" />
            </Trigger>
        </Style.Triggers>
    </Style>

    <!-- Default Button style — neutral/bordered, used as-is by every button that
         isn't the one primary call-to-action on its panel (Start Enrollment). -->
    <Style TargetType="Button">
        <Setter Property="Background" Value="{StaticResource SurfaceBrush}" />
        <Setter Property="Foreground" Value="{StaticResource InkBrush}" />
        <Setter Property="BorderBrush" Value="{StaticResource BorderColorBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="FontSize" Value="15" />
        <Setter Property="FontFamily" Value="{StaticResource FigtreeFont}" />
        <Setter Property="Padding" Value="16,10" />
        <Setter Property="MinHeight" Value="48" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="8">
                        <ContentPresenter HorizontalAlignment="Center"
                                          VerticalAlignment="Center"
                                          Margin="{TemplateBinding Padding}" />
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="BorderBrush" Value="{StaticResource AccentBrush}" />
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Opacity" Value="0.5" />
            </Trigger>
        </Style.Triggers>
    </Style>

    <!-- The one primary call-to-action style (Start Enrollment). BasedOn the
         implicit Button style above so it inherits the template/sizing and only
         overrides color — its own IsMouseOver trigger sets Background too (the
         base style's hover trigger only touches BorderBrush), and being the
         later/derived style's trigger, it wins for the properties it sets. -->
    <Style x:Key="PrimaryButtonStyle" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
        <Setter Property="Background" Value="{StaticResource AccentBrush}" />
        <Setter Property="Foreground" Value="{StaticResource AccentInkBrush}" />
        <Setter Property="BorderBrush" Value="{StaticResource AccentBrush}" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Background" Value="{StaticResource AccentHoverBrush}" />
                <Setter Property="BorderBrush" Value="{StaticResource AccentHoverBrush}" />
            </Trigger>
        </Style.Triggers>
    </Style>

</ResourceDictionary>
```

- [ ] **Step 4: Merge the dictionary into `App.xaml`**

```xml
<!-- agent/src/AttendanceAgent/App.xaml — full file -->
<Application x:Class="AttendanceAgent.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Styles.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **Step 5: Build and confirm nothing broke**

Run: `dotnet build agent/AttendanceAgent.sln`
Expected: 0 errors. (Existing tests are unaffected by this task — no `.cs` file changed — but run `dotnet test agent/tests/AttendanceAgent.Tests` anyway as a baseline before Task 2 touches `MainWindow.xaml`, which `HostCompositionTests.cs` constructs directly.)
Expected: all existing tests still pass.

- [ ] **Step 6: Commit**

```bash
git add agent/src/AttendanceAgent/Fonts agent/src/AttendanceAgent/Assets agent/src/AttendanceAgent/Styles.xaml agent/src/AttendanceAgent/AttendanceAgent.csproj agent/src/AttendanceAgent/App.xaml
git commit -m "feat: add Sefed brand fonts, logo assets, and WPF style resources"
```

---

### Task 2: `MainWindow.xaml` — header band, resize, restyle

**Files:**
- Modify: `agent/src/AttendanceAgent/MainWindow.xaml`

**Interfaces:**
- Consumes: Task 1's resources (`SurfaceMutedBrush`, `InkBrush`, `AccentBrush`, `FigtreeFont`, `ChakraPetchFont`, `PrimaryButtonStyle`, `/Assets/sefed-icon.png`). Every `{Binding ...}` in the file today is UNCHANGED — this task only adds/edits presentation attributes (`Style`, `Background`, `Foreground`, `FontFamily`, `FontSize`, sizes/margins) and one new header `Border`.

- [ ] **Step 1: Rewrite the window**

```xml
<!-- agent/src/AttendanceAgent/MainWindow.xaml — full file -->
<Window x:Class="AttendanceAgent.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Sefed Attendance" Height="480" Width="560"
        Icon="Assets/sefed-icon-dark.png"
        Background="{StaticResource SurfaceMutedBrush}"
        FontFamily="{StaticResource FigtreeFont}">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="64" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <Border Grid.Row="0" Background="{StaticResource InkBrush}">
            <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="20,0">
                <Image Source="Assets/sefed-icon.png" Width="32" Height="32" />
                <StackPanel Margin="10,0,0,0" VerticalAlignment="Center">
                    <TextBlock Text="Sefed" Foreground="White" FontSize="16" FontWeight="Medium" />
                    <TextBlock Text="ATTENDANCE" Foreground="{StaticResource AccentBrush}"
                               FontFamily="{StaticResource ChakraPetchFont}" FontSize="10"
                               CharacterSpacing="200" />
                </StackPanel>
            </StackPanel>
        </Border>

        <Grid Grid.Row="1">
            <StackPanel Margin="24" Visibility="{Binding PunchPanelVisibility}">
                <TextBlock Text="Employee ID / PIN" Margin="0,0,0,6" FontSize="14"
                           Foreground="{StaticResource InkSoftBrush}" />
                <TextBox Text="{Binding EmployeeCode, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,20" />
                <WrapPanel>
                    <Button Content="IN" Command="{Binding PunchCommand}" CommandParameter="In" Margin="0,0,10,10" MinWidth="110" />
                    <Button Content="BREAK OUT" Command="{Binding PunchCommand}" CommandParameter="BreakOut" Margin="0,0,10,10" MinWidth="110" />
                    <Button Content="BREAK IN" Command="{Binding PunchCommand}" CommandParameter="BreakIn" Margin="0,0,10,10" MinWidth="110" />
                    <Button Content="OUT" Command="{Binding PunchCommand}" CommandParameter="Out" Margin="0,0,10,10" MinWidth="110" />
                </WrapPanel>
                <Button Content="Admin" Command="{Binding AdminLoginCommand}" Margin="0,16,0,0"
                        HorizontalAlignment="Left" FontSize="12" Padding="10,6" MinHeight="0" />
                <TextBlock Text="{Binding StatusMessage}" Margin="0,20,0,0" TextWrapping="Wrap"
                           FontSize="14" Foreground="{StaticResource InkBrush}" />
            </StackPanel>

            <StackPanel Margin="24" Visibility="{Binding EnrollPanelVisibility}">
                <TextBlock Text="Enroll fingerprint — Employee ID / PIN" Margin="0,0,0,6" FontSize="14"
                           Foreground="{StaticResource InkSoftBrush}" />
                <TextBox Text="{Binding EnrollEmployeeCode, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,20" />
                <WrapPanel>
                    <Button Content="Start Enrollment" Command="{Binding StartEnrollmentCommand}"
                            Style="{StaticResource PrimaryButtonStyle}" Margin="0,0,10,10" MinWidth="160" />
                    <Button Content="Cancel" Command="{Binding StartEnrollmentCancelCommand}" Margin="0,0,10,10" MinWidth="110" />
                    <Button Content="Back" Command="{Binding ExitAdminModeCommand}" Margin="0,0,10,10" MinWidth="110" />
                </WrapPanel>
                <TextBlock Text="{Binding EnrollProgressMessage}" Margin="0,20,0,0" TextWrapping="Wrap"
                           FontSize="14" Foreground="{StaticResource InkBrush}" />
                <TextBlock Text="{Binding StatusMessage}" Margin="0,8,0,0" TextWrapping="Wrap"
                           FontSize="14" Foreground="{StaticResource DangerBrush}" />
            </StackPanel>
        </Grid>
    </Grid>
</Window>
```

Two notes on deliberate choices here, for whoever reviews this:
- The Enroll panel's final `StatusMessage` `TextBlock` is given `Foreground="{StaticResource DangerBrush}"` (red) while the Punch panel's `StatusMessage` keeps `InkBrush` (neutral) — this is a SMALL asymmetry: the Enroll panel's status message is only ever shown after `StartEnrollmentAsync` completes (success OR failure, per `MainViewModel.cs`), so painting it red unconditionally is WRONG for the success case ("Fingerprint enrolled for X." should not look like an error). Fix this before considering the task done: bind `Foreground` conditionally, or — simpler, given there's no existing `IValueConverter` infrastructure in this codebase and adding one just for this would be disproportionate — leave BOTH status messages `InkBrush` (neutral) for now, matching the Punch panel's existing (correct) treatment, and note this as a follow-up if a genuine failure/success color distinction is wanted later. Do not ship the unconditional red version.
- `CharacterSpacing="200"` on the "ATTENDANCE" sub-label approximates the portal's `tracking-[0.2em]` letter-spacing; WPF's `CharacterSpacing` unit is 1/1000 em, so `200` ≈ `0.2em` — close enough visually, exact conversion isn't critical for a decorative label.

- [ ] **Step 2: Fix the status-message color issue flagged above**

Apply the "leave both `InkBrush`" fix directly in the XAML from Step 1 before moving on — change the Enroll panel's last `TextBlock`'s `Foreground` from `{StaticResource DangerBrush}` to `{StaticResource InkBrush}`, matching the Punch panel's treatment.

- [ ] **Step 3: Build, run the tests, and visually verify**

Run: `dotnet build agent/AttendanceAgent.sln`
Expected: 0 errors.

Run: `dotnet test agent/tests/AttendanceAgent.Tests`
Expected: all tests pass, including `HostCompositionTests.Build_And_Resolve_MainWindow_And_MainViewModel_From_A_Scope_Succeeds` (which actually constructs `MainWindow` via `InitializeComponent()` on a real STA thread — this is the test most likely to catch a genuine XAML parse error, like a missing resource key or a malformed `ControlTemplate`, that `dotnet build` alone might not).

Run the app directly: `dotnet run --project agent/src/AttendanceAgent` (Debug build — uses the fakes, no hardware needed) and visually confirm: 560×480 window, dark teal header with the icon + Sefed/ATTENDANCE lockup, larger bordered inputs/buttons, gold border on button hover, gold focus ring on the text box, "Admin" as a small subtle button, clicking Admin and entering any credentials (Debug's `FakeBackendApiClient`/fakes won't actually authenticate against a real backend, so seeing the "Admin login failed." message is the expected and correct result here — the point of this check is confirming the Enroll panel LAYOUT renders correctly, not exercising real auth).

- [ ] **Step 4: Commit**

```bash
git add agent/src/AttendanceAgent/MainWindow.xaml
git commit -m "feat: restyle the desktop agent window with the Sefed brand"
```
