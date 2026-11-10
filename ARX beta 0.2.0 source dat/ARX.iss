#ifndef Payload
  #error Pass /DPayload=<publish folder>
#endif

[Setup]
AppId={{9F5CB164-814E-4346-A21A-E3171454CEFA}
AppName=ARX
AppVersion=1.0.7-beta
AppVerName=ARX 1.0.7 beta
AppPublisher=ARX
AppPublisherURL=https://github.com/Kadjiru/ATX
DefaultDirName={localappdata}\Programs\ARX
DefaultGroupName=ARX
PrivilegesRequired=lowest
SetupArchitecture=x64
MinVersion=10.0.17763
SetupMutex=Local\ARX.Setup
OutputBaseFilename=ARX-Setup-1.0.7-beta
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
ru.NpcapSubtitle=Драйвер захвата Npcap не найден на этом компьютере.
ru.NpcapText=ARX может загрузить Npcap с сайта разработчика, проверить файл и открыть установщик драйвера. Windows может запросить права администратора. Атлас и сохранённые отчёты доступны без драйвера.
ru.NpcapInstall=Скачать и установить Npcap
ru.NpcapSkip=Продолжить без Npcap
ru.NpcapDownloading=Загрузка Npcap
ru.NpcapDownloadDescription=Получаем официальный установщик и проверяем контрольную сумму.
ru.NpcapDownloadFailed=Не удалось загрузить или проверить Npcap. Проверьте подключение и попробуйте снова или продолжите без драйвера.
ru.NpcapCanceled=Установка Npcap отменена. Можно повторить попытку или продолжить без драйвера.
ru.NpcapFailed=Установка Npcap не завершилась. Код: %1. Можно повторить попытку или продолжить без драйвера.
ru.NpcapMissing=Установщик завершился, но ARX не обнаружил драйвер. Проверьте Npcap после перезагрузки или продолжите без него.
ru.NpcapRestart=Npcap установлен. Перезагрузите Windows, когда будет удобно, чтобы включить живую статистику.
ru.UpdateTitle=Установка обновления
ru.UpdateFiles=Обновляем файлы ARX
ru.UpdateLaunch=Запускаем новую версию ARX
en.Desktop=Create a desktop shortcut
en.Launch=Open ARX
en.Guide=ARX user guide
en.Missing=Windows is missing components required to run ARX:
en.Running=Close ARX to continue the installation.
en.NpcapTitle=Npcap for live combat
en.NpcapSubtitle=The Npcap packet capture driver was not found on this PC.
en.NpcapText=ARX can download Npcap from its publisher, verify the file, and open the driver installer. Windows may ask for administrator approval. The atlas and saved reports work without the driver.
en.NpcapInstall=Download and install Npcap
en.NpcapSkip=Continue without Npcap
en.NpcapDownloading=Downloading Npcap
en.NpcapDownloadDescription=Downloading the official installer and verifying its checksum.
en.NpcapDownloadFailed=ARX could not download or verify Npcap. Check your connection and try again, or continue without the driver.
en.NpcapCanceled=Npcap installation was canceled. You can try again or continue without the driver.
en.NpcapFailed=Npcap installation did not finish. Code: %1. You can try again or continue without the driver.
en.NpcapMissing=The installer finished, but ARX could not detect the driver. Check Npcap after a reboot or continue without it.
en.NpcapRestart=Npcap was installed. Restart Windows when convenient to enable live combat statistics.
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
const
  NpcapFileName = 'npcap-1.90.exe';
  NpcapUrl = 'https://npcap.com/dist/npcap-1.90.exe';
  // Verified against the publisher's Authenticode-signed download before packaging ARX.
  NpcapSha256 = '682b57e1c05ef375931975c9d6ded40888f4bfdf9f4b11571e654f962f1199b6';

var
  NpcapPage: TInputOptionWizardPage;
  NpcapDownload: TDownloadWizardPage;
  UpdateForm: TSetupForm;
  UpdateMessage: TLabel;
  UpdateFill: TPanel;

function NpcapPresent(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\npcap') and
    (FileExists(ExpandConstant('{sys}\Npcap\wpcap.dll')) or FileExists(ExpandConstant('{sys}\wpcap.dll')));
end;

procedure InitializeWizard();
begin
  NpcapPage := CreateInputOptionPage(wpSelectTasks, CustomMessage('NpcapTitle'), CustomMessage('NpcapSubtitle'),
    CustomMessage('NpcapText'), True, False);
  NpcapPage.Add(CustomMessage('NpcapInstall'));
  NpcapPage.Add(CustomMessage('NpcapSkip'));
  NpcapPage.SelectedValueIndex := 0;
  NpcapDownload := CreateDownloadPage(CustomMessage('NpcapDownloading'), CustomMessage('NpcapDownloadDescription'), nil);
  NpcapDownload.ShowBaseNameInsteadOfUrl := True;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = NpcapPage.ID) and (WizardSilent or NpcapPresent());
end;

function DownloadNpcap(): Boolean;
begin
  Result := False;
  NpcapDownload.Clear;
  NpcapDownload.Add(NpcapUrl, NpcapFileName, NpcapSha256);
  NpcapDownload.Show;
  try
    try
      NpcapDownload.Download;
      Result := True;
    except
      Log('Npcap download: ' + GetExceptionMessage);
    end;
  finally
    NpcapDownload.Hide;
  end;
  if not Result and not NpcapDownload.AbortedByUser then
    MsgBox(CustomMessage('NpcapDownloadFailed'), mbError, MB_OK);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Installer: String;
  Code: Integer;
  Started: Boolean;
begin
  Result := True;
  if (CurPageID <> NpcapPage.ID) or WizardSilent or (NpcapPage.SelectedValueIndex <> 0) or NpcapPresent() then
    Exit;
  Result := False;
  if not DownloadNpcap() then
    Exit;
  if NpcapPresent() then
  begin
    Result := True;
    Exit;
  end;
  Installer := ExpandConstant('{tmp}\') + NpcapFileName;
  Started := ShellExec('runas', Installer, '', '', SW_SHOWNORMAL, ewWaitUntilTerminated, Code);
  Log(Format('Npcap installer result: %d', [Code]));
  if Started and (Code = 3010) then
  begin
    MsgBox(CustomMessage('NpcapRestart'), mbInformation, MB_OK);
    Result := True;
  end
  else if NpcapPresent() then
    Result := True
  else if (Started and (Code = 1)) or (not Started and (Code = 1223)) then
    MsgBox(CustomMessage('NpcapCanceled'), mbInformation, MB_OK)
  else if Started and (Code = 0) then
    MsgBox(CustomMessage('NpcapMissing'), mbError, MB_OK)
  else
    MsgBox(FmtMessage(CustomMessage('NpcapFailed'), [IntToStr(Code)]), mbError, MB_OK);
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
