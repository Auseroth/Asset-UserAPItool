!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"

Name "Connexus"
OutFile "Connexus-Setup.exe"
InstallDir "$PROGRAMFILES64\Connexus"
InstallDirRegKey HKLM "Software\Connexus" "InstallDir"
RequestExecutionLevel admin
ShowInstDetails show
ShowUnInstDetails show

!define APP_PROJECT_DIR "..\LdapCloudSync.App\bin\Release\net8.0-windows"
!define SERVICE_PROJECT_DIR "..\LdapCloudSync.Service\bin\Release\net8.0-windows"
!define CONFIG_LAUNCHER_DIR "..\LdapCloudSync.ConfigLauncher\bin\Release\net8.0-windows"

!define APP_EXE "Connexus.exe"
!define SERVICE_EXE "LdapCloudSync.Service.exe"
!define CONFIG_LAUNCHER_EXE "LdapCloudSync.ConfigLauncher.exe"

!define SERVICE_NAME "LdapCloudSync"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

Section "Install"
	SetOutPath "$INSTDIR"
	WriteRegStr HKLM "Software\Connexus" "InstallDir" "$INSTDIR"

	; Main app
	SetOutPath "$INSTDIR"
	File "${APP_PROJECT_DIR}\${APP_EXE}"
	File "${APP_PROJECT_DIR}\LdapCloudSync.App.dll"
	File "${APP_PROJECT_DIR}\LdapCloudSync.App.deps.json"
	File "${APP_PROJECT_DIR}\LdapCloudSync.App.runtimeconfig.json"
	File "${APP_PROJECT_DIR}\LdapCloudSync.Core.dll"

	; Optional config launcher if published
	IfFileExists "${CONFIG_LAUNCHER_DIR}\${CONFIG_LAUNCHER_EXE}" 0 +4
	  File "${CONFIG_LAUNCHER_DIR}\${CONFIG_LAUNCHER_EXE}"
	  File "${CONFIG_LAUNCHER_DIR}\LdapCloudSync.ConfigLauncher.dll"
	  File "${CONFIG_LAUNCHER_DIR}\LdapCloudSync.ConfigLauncher.deps.json"
	  File "${CONFIG_LAUNCHER_DIR}\LdapCloudSync.ConfigLauncher.runtimeconfig.json"

	; Service
	SetOutPath "$INSTDIR\Service"
	File "${SERVICE_PROJECT_DIR}\${SERVICE_EXE}"
	File "${SERVICE_PROJECT_DIR}\LdapCloudSync.Service.dll"
	File "${SERVICE_PROJECT_DIR}\LdapCloudSync.Service.deps.json"
	File "${SERVICE_PROJECT_DIR}\LdapCloudSync.Service.runtimeconfig.json"
	File "${SERVICE_PROJECT_DIR}\LdapCloudSync.Core.dll"
	File "${SERVICE_PROJECT_DIR}\install-service.ps1"
	File "${SERVICE_PROJECT_DIR}\uninstall-service.ps1"

	; Shortcuts
	CreateDirectory "$SMPROGRAMS\Connexus"
	CreateShortCut "$SMPROGRAMS\Connexus\Connexus - Kiosk.lnk" "$INSTDIR\${APP_EXE}"
	IfFileExists "$INSTDIR\${CONFIG_LAUNCHER_EXE}" 0 +3
	  CreateShortCut "$SMPROGRAMS\Connexus\Connexus - Admin Config.lnk" "$INSTDIR\${CONFIG_LAUNCHER_EXE}"
	IfFileExists "$INSTDIR\${CONFIG_LAUNCHER_EXE}" +2 0
	  CreateShortCut "$SMPROGRAMS\Connexus\Connexus - Admin Config.lnk" "$INSTDIR\${APP_EXE}" "--config"

	; Service install helper shortcut
	CreateShortCut "$SMPROGRAMS\Connexus\Install Connexus Service.lnk" "$WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" "-ExecutionPolicy Bypass -File \"$INSTDIR\Service\install-service.ps1\""

	; Desktop shortcuts
	CreateShortCut "$DESKTOP\Connexus - Kiosk.lnk" "$INSTDIR\${APP_EXE}"
	IfFileExists "$INSTDIR\${CONFIG_LAUNCHER_EXE}" 0 +3
	  CreateShortCut "$DESKTOP\Connexus - Admin Config.lnk" "$INSTDIR\${CONFIG_LAUNCHER_EXE}"
	IfFileExists "$INSTDIR\${CONFIG_LAUNCHER_EXE}" +2 0
	  CreateShortCut "$DESKTOP\Connexus - Admin Config.lnk" "$INSTDIR\${APP_EXE}" "--config"

	; Optional service install during setup
	nsExec::ExecToLog '"$WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -ExecutionPolicy Bypass -File "$INSTDIR\Service\install-service.ps1"'
SectionEnd

Section "Uninstall"
	; Attempt to remove service via PowerShell helper if present
	IfFileExists "$INSTDIR\Service\uninstall-service.ps1" 0 +2
	  nsExec::ExecToLog '"$WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -ExecutionPolicy Bypass -File "$INSTDIR\Service\uninstall-service.ps1"'

	Delete "$DESKTOP\Connexus - Kiosk.lnk"
	Delete "$DESKTOP\Connexus - Admin Config.lnk"

	Delete "$SMPROGRAMS\Connexus\Connexus - Kiosk.lnk"
	Delete "$SMPROGRAMS\Connexus\Connexus - Admin Config.lnk"
	Delete "$SMPROGRAMS\Connexus\Install Connexus Service.lnk"
	RMDir "$SMPROGRAMS\Connexus"

	Delete "$INSTDIR\Service\*.*"
	RMDir "$INSTDIR\Service"

	Delete "$INSTDIR\${APP_EXE}"
	Delete "$INSTDIR\LdapCloudSync.App.dll"
	Delete "$INSTDIR\LdapCloudSync.App.deps.json"
	Delete "$INSTDIR\LdapCloudSync.App.runtimeconfig.json"
	Delete "$INSTDIR\LdapCloudSync.Core.dll"
	Delete "$INSTDIR\${CONFIG_LAUNCHER_EXE}"
	Delete "$INSTDIR\LdapCloudSync.ConfigLauncher.dll"
	Delete "$INSTDIR\LdapCloudSync.ConfigLauncher.deps.json"
	Delete "$INSTDIR\LdapCloudSync.ConfigLauncher.runtimeconfig.json"

	DeleteRegKey HKLM "Software\LdapCloudSync"
	RMDir "$INSTDIR"
SectionEnd
