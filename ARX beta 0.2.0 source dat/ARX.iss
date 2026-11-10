#ifndef Payload
  #error Pass /DPayload=<publish folder>
#endif

[Setup]
AppId={{9F5CB164-814E-4346-A21A-E3171454CEFA}
AppName=ARX
AppVersion=1.0.3-beta
AppVerName=ARX 1.0.3 beta
AppPublisher=ARX
AppPublisherURL=https://github.com/Kadjiru/ATX
DefaultDirName={localappdata}\Programs\ARX
DefaultGroupName=ARX
PrivilegesRequired=lowest
SetupArchitecture=x64
MinVersion=10.0.17763
SetupMutex=Local\ARX.Setup
OutputBaseFilename=ARX-Setup-1.0.3-beta
SetupIconFile=Assets\ARX.ico
UninstallDisplayIcon={app}\ARX.Beta.exe
WizardStyle=modern dark hidebevels
WizardBackColor=#122126
WizardImageFile=Assets\Wizard.png
WizardSmallImageFile=Assets\WizardSmall.png
LicenseFile=LICENSE
DisableProgramGroupPage=yes
Compression=lzma2/max
SolidCompression=yes

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
ru.Desktop=Создать ярлык на рабочем столе
ru.Launch=Открыть ARX
ru.Guide=Инструкция ARX
ru.Missing=В Windows отсутствуют компоненты, необходимые для запуска ARX:
ru.Running=Закройте ARX, чтобы продолжить установку.
ru.NpcapTitle=Npcap для живой статистики
ru.NpcapSubtitle=ARX проверил, установлен ли драйвер захвата.
ru.NpcapText=Npcap не найден. ARX установится и сможет открывать карту, таймеры и сохранённые отчёты. Для живого боя установите Npcap отдельно с настройками по умолчанию.
ru.NpcapOpen=Открыть npcap.com
ru.UpdateTitle=Установка обновления
ru.UpdateFiles=Обновляем файлы ARX
ru.UpdateLaunch=Запускаем новую версию ARX
en.Desktop=Create a desktop shortcut
en.Launch=Open ARX
en.Guide=ARX user guide
en.Missing=Windows is missing components required to run ARX:
en.Running=Close ARX to continue the installation.
en.NpcapTitle=Npcap for live combat
en.NpcapSubtitle=ARX checked for the packet capture driver.
en.NpcapText=Npcap was not found. ARX can use the atlas, timers and saved reports. For live combat, install Npcap separately with its default settings.
en.NpcapOpen=Open npcap.com
en.UpdateTitle=Installing the update
en.UpdateFiles=Updating ARX files
en.UpdateLaunch=Launching the new ARX version

[Tasks]
Name: "desktop"; Description: "{cm:Desktop}"; Flags: unchecked

[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Assets\UpdateBackdrop.png"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\ARX"; Filename: "{app}\ARX.Beta.exe"; WorkingDir: "{app}"
Name: "{autoprograms}\{cm:Guide}"; Filename: "{app}\INSTALL.txt"
Name: "{autodesktop}\ARX"; Filename: "{app}\ARX.Beta.exe"; WorkingDir: "{app}"; Tasks: desktop

[Run]
Filename: "{app}\ARX.Beta.exe"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent unchecked
Filename: "{app}\ARX.Beta.exe"; Parameters: "--updated"; WorkingDir: "{app}"; Flags: nowait; Check: IsArxUpdate

[Code]
var
  NpcapPage: TWizardPage;
  UpdateForm: TSetupForm;
  UpdateMessage: TLabel;
  UpdateFill: TPanel;

function NpcapPresent(): Boolean;
begin
  Result := FileExists(ExpandConstant('{sys}\Npcap\wpcap.dll')) or FileExists(ExpandConstant('{sys}\wpcap.dll'));
end;

procedure OpenNpcap(Sender: TObject);
var
  ErrorCode: Integer;
begin
  ShellExec('open', 'https://npcap.com/#download', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

procedure InitializeWizard();
var
  Info: TNewStaticText;
  Link: TNewButton;
begin
  NpcapPage := CreateCustomPage(wpSelectTasks, CustomMessage('NpcapTitle'), CustomMessage('NpcapSubtitle'));
  Info := TNewStaticText.Create(NpcapPage);
  Info.Parent := NpcapPage.Surface;
  Info.AutoSize := False;
  Info.WordWrap := True;
  Info.Width := NpcapPage.SurfaceWidth;
  Info.Height := ScaleY(72);
  Info.Caption := CustomMessage('NpcapText');
  Link := TNewButton.Create(NpcapPage);
  Link.Parent := NpcapPage.Surface;
  Link.Top := ScaleY(84);
  Link.Width := ScaleX(156);
  Link.Height := ScaleY(28);
  Link.Caption := CustomMessage('NpcapOpen');
  Link.OnClick := @OpenNpcap;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = NpcapPage.ID) and (NpcapPresent() or WizardSilent);
end;

procedure CheckComponent(const FileName: String; var Missing: String);
begin
  if not FileExists(ExpandConstant('{sys}\') + FileName) then
    Missing := Missing + #13#10 + ' • ' + FileName;
end;

function IsArxUpdate(): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/ARXUPDATE') = 0 then
      Result := True;
end;

function UpdateLabel(const Caption: String; X, Y, FontSize: Integer; Color: TColor; Bold: Boolean): TLabel;
begin
  Result := TLabel.Create(UpdateForm);
  Result.Parent := UpdateForm;
  Result.Left := ScaleX(X);
  Result.Top := ScaleY(Y);
  Result.Transparent := True;
  Result.Caption := Caption;
  Result.Font.Name := 'Segoe UI';
  Result.Font.Size := FontSize;
  Result.Font.Color := Color;
  if Bold then
    Result.Font.Style := [fsBold];
end;

procedure ShowUpdateProgress();
var
  Background: TBitmapImage;
  Track: TPanel;
begin
  UpdateForm := CreateCustomForm(ScaleX(720), ScaleY(360), False, False);
  UpdateForm.BorderStyle := bsNone;
  UpdateForm.Position := poScreenCenter;
  UpdateForm.Caption := 'ARX · ' + CustomMessage('UpdateTitle');
  Background := TBitmapImage.Create(UpdateForm);
  Background.Parent := UpdateForm;
  Background.SetBounds(0, 0, UpdateForm.ClientWidth, UpdateForm.ClientHeight);
  Background.Stretch := True;
  ExtractTemporaryFile('UpdateBackdrop.png');
  Background.PngImage.LoadFromFile(ExpandConstant('{tmp}\UpdateBackdrop.png'));
  UpdateLabel('ARX  /  AION 2 GLOBAL', 35, 83, 9, $00C4BAB2, True);
  UpdateLabel('◈  ARX', 35, 101, 41, $00CEDF89, True);
  UpdateLabel(CustomMessage('UpdateTitle'), 35, 175, 16, $00F6F5E9, True);
  Track := TPanel.Create(UpdateForm);
  Track.Parent := UpdateForm;
  Track.BevelOuter := bvNone;
  Track.Color := $00383220;
  Track.SetBounds(ScaleX(35), ScaleY(235), ScaleX(328), ScaleY(5));
  UpdateFill := TPanel.Create(Track);
  UpdateFill.Parent := Track;
  UpdateFill.BevelOuter := bvNone;
  UpdateFill.Color := $00CEDF89;
  UpdateFill.SetBounds(0, 0, 0, Track.Height);
  UpdateMessage := UpdateLabel(CustomMessage('UpdateFiles') + ' · 0%', 35, 250, 10, $00C4BAB2, False);
  UpdateForm.Show;
  UpdateForm.Update;
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
var
  Percent: Integer;
begin
  if (UpdateForm = nil) or (MaxProgress <= 0) then
    Exit;
  Percent := Round(100.0 * CurProgress / MaxProgress);
  UpdateFill.Width := ScaleX(328) * Percent div 100;
  UpdateMessage.Caption := CustomMessage('UpdateFiles') + ' · ' + IntToStr(Percent) + '%';
  UpdateForm.Update;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssInstall) and WizardSilent and IsArxUpdate() then
    ShowUpdateProgress();
  if (CurStep = ssPostInstall) and (UpdateForm <> nil) then
  begin
    UpdateFill.Width := ScaleX(328);
    UpdateMessage.Caption := CustomMessage('UpdateLaunch');
    UpdateForm.Update;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  I: Integer;
begin
  Result := '';
  if IsArxUpdate() then
    for I := 1 to 100 do
    begin
      if not CheckForMutexes('Local\ARX.Legion.Beta') then
        Exit;
      Sleep(200);
    end;
  if CheckForMutexes('Local\ARX.Legion.Beta') then
    Result := CustomMessage('Running');
end;

function InitializeSetup(): Boolean;
var
  Missing: String;
begin
  Missing := '';
  CheckComponent('ucrtbase.dll', Missing);
  CheckComponent('dwrite.dll', Missing);
  CheckComponent('d3d11.dll', Missing);
  CheckComponent('dxgi.dll', Missing);
  Result := Missing = '';
  if not Result then
    MsgBox(CustomMessage('Missing') + Missing, mbCriticalError, MB_OK);
end;
