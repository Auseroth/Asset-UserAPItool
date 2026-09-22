!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "nsDialogs.nsh"
!include "LogicLib.nsh"

!define APPNAME "ConNexus"
!define EXENAME "ConNexus.exe"
!define INSTALLDIR "$PROGRAMFILES64\${APPNAME}"
!define SOLUTION_OUTPUT_DIR "C:\\temp file transfer\\9.VisualStudio\\field testing\\Asset&UserAPItool\\installer"

; FolderProfile.pubxml publishes the App project self-contained to this folder;
; its AfterTargets also merges ConfigLauncher into the root and Service into a
; Service\ subfolder here, so this one folder has everything to install.
!define PUBLISH_DIR "C:\temp file transfer\9.VisualStudio\field testing\Asset&UserAPItool\LdapCloudSync.App\bin\Release\net8.0-windows\publish\win-x86"

!define APP_EXE "ConNexus.exe"
!define SERVICE_EXE "ConNexus.Service.exe"
!define CONFIG_LAUNCHER_EXE "ConNexus.ConfigLauncher.exe"
!define SERVICE_NAME "ConNexus"
!define VERSION "1.0.0.0"
!define APP_ICON "${PUBLISH_DIR}\Connexus.ico"

Name "${APPNAME}"
OutFile "NSIS output\${APPNAME}_Install.exe"
InstallDir "${INSTALLDIR}"
InstallDirRegKey HKLM "Software\${APPNAME}" "InstallDir"
RequestExecutionLevel admin
Icon "${APP_ICON}"
UninstallIcon "${APP_ICON}"
ShowInstDetails show
ShowUnInstDetails show


!system 'cmd /C if not exist "${SOLUTION_OUTPUT_DIR}" mkdir "${SOLUTION_OUTPUT_DIR}"'
!system 'cmd /C copy /Y "${__FILE__}" "${SOLUTION_OUTPUT_DIR}\\${APPNAME}.nsi" >nul'
!finalize 'cmd /C copy /Y "%1" "${SOLUTION_OUTPUT_DIR}\\${APPNAME}_Install.exe" >nul'

!define MUI_NO_DEFAULT_BUTTONS
!define MUI_NO_DEFAULT_BRANDING
!define MUI_ABORTWARNING

Var MAINTENANCE_MODE
Var MAINTENANCE_CHOICE
Var STARTUP_CHECKED
Var DESKTOP_CHECKED

Function CloseAppIfRunning
    nsExec::ExecToLog 'taskkill /F /IM "${APP_EXE}"'
    Sleep 1000
FunctionEnd

Function WelcomePagePre
    StrCmp $MAINTENANCE_MODE "" show_welcome
    Abort
    show_welcome:
FunctionEnd

Function MaintenancePageLeave
    ${NSD_GetState} $1 $MAINTENANCE_CHOICE
    ${If} $MAINTENANCE_CHOICE == ${BST_CHECKED}
        StrCpy $MAINTENANCE_CHOICE "update"
    ${Else}
        ${NSD_GetState} $2 $MAINTENANCE_CHOICE
        ${If} $MAINTENANCE_CHOICE == ${BST_CHECKED}
            StrCpy $MAINTENANCE_CHOICE "uninstall"
            MessageBox MB_YESNO|MB_ICONQUESTION "Are you sure you want to uninstall ${APPNAME}?" IDYES mpl_confirmed
            Abort
            mpl_confirmed:
            Call CloseAppIfRunning
            ExecWait '"$INSTDIR\Uninstall.exe"'
            Quit
        ${Else}
            StrCpy $MAINTENANCE_CHOICE "cancel"
            Quit
        ${EndIf}
    ${EndIf}
FunctionEnd

Function .onInit
    ReadRegStr $MAINTENANCE_MODE HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayName"
FunctionEnd

Function MaintenancePageShow
    StrCmp $MAINTENANCE_MODE "${APPNAME}" 0 skip
    !insertmacro MUI_HEADER_TEXT "${APPNAME} Maintenance" "Choose an action:"
    nsDialogs::Create 1018
    Pop $0
    ${If} $0 == error
        Abort
    ${EndIf}
    ${NSD_CreateRadioButton} 0u 20u 100% 12u "Update/Repair ${APPNAME}"
    Pop $1
    ${NSD_SetState} $1 ${BST_CHECKED}
    ${NSD_CreateRadioButton} 0u 40u 100% 12u "Uninstall ${APPNAME}"
    Pop $2
    ${NSD_CreateRadioButton} 0u 60u 100% 12u "Cancel"
    Pop $3
    nsDialogs::Show
    Return
    skip:
        Abort
FunctionEnd

Function OptionsPageShow
    StrCmp $MAINTENANCE_MODE "${APPNAME}" 0 show_options
    StrCmp $MAINTENANCE_CHOICE "update" skip_options
    Goto show_options
    skip_options:
        Abort
    show_options:
    !insertmacro MUI_HEADER_TEXT "Installation Options" "Choose additional tasks:"
    nsDialogs::Create 1018
    Pop $0
    ${If} $0 == error
        Abort
    ${EndIf}
    ${NSD_CreateCheckbox} 0u 20u 100% 12u "Add to Startup (all users)"
    Pop $1
    ${NSD_SetState} $1 $STARTUP_CHECKED
    ${NSD_CreateCheckbox} 0u 40u 100% 12u "Add shortcut to Public Desktop"
    Pop $2
    ${NSD_SetState} $2 $DESKTOP_CHECKED
    nsDialogs::Show
FunctionEnd

Function OptionsPageLeave
    ${NSD_GetState} $1 $STARTUP_CHECKED
    ${NSD_GetState} $2 $DESKTOP_CHECKED
FunctionEnd

!define MUI_PAGE_CUSTOMFUNCTION_PRE WelcomePagePre
!insertmacro MUI_PAGE_WELCOME
!undef MUI_PAGE_CUSTOMFUNCTION_PRE

Page custom MaintenancePageShow MaintenancePageLeave
Page custom OptionsPageShow OptionsPageLeave

!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Run ${APPNAME}"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

VIProductVersion "${VERSION}"
VIAddVersionKey "CompanyName" "LdapCloudSync"
VIAddVersionKey "LegalCopyright" "Copyright (c) 2026"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "Author" "LdapCloudSync"
VIAddVersionKey "FileDescription" "${APPNAME} installer"
VIAddVersionKey "InternalName" "${APPNAME}"

Section "Install"
    ; Stop the service first so its files aren't locked when we overwrite them.
    nsExec::ExecToLog 'net stop "${SERVICE_NAME}"'

    StrCmp $MAINTENANCE_MODE "${APPNAME}" is_maintenance
    Goto do_install

    is_maintenance:
        Call CloseAppIfRunning
        Goto update_only

    update_only:
        SetOutPath "$INSTDIR"
        WriteRegStr HKLM "Software\${APPNAME}" "InstallDir" "$INSTDIR"

        ; Full self-contained publish output (app, runtime deps, locale folders,
        ; plus the merged ConfigLauncher files and Service\ subfolder) in one shot.
        File /r "${PUBLISH_DIR}\*.*"

        SetShellVarContext all
        CreateDirectory "$SMPROGRAMS\${APPNAME}"
        CreateShortCut "$SMPROGRAMS\${APPNAME}\${APPNAME} - Kiosk.lnk" "$INSTDIR\${APP_EXE}"
        IfFileExists "$INSTDIR\${CONFIG_LAUNCHER_EXE}" 0 no_admin_shortcut_programs_update
            CreateShortCut "$SMPROGRAMS\${APPNAME}\${APPNAME} - Admin Config.lnk" "$INSTDIR\${CONFIG_LAUNCHER_EXE}"
            Goto after_admin_shortcut_programs_update
        no_admin_shortcut_programs_update:
            CreateShortCut "$SMPROGRAMS\${APPNAME}\${APPNAME} - Admin Config.lnk" "$INSTDIR\${APP_EXE}" "--config"
        after_admin_shortcut_programs_update:
        CreateShortCut "$SMPROGRAMS\${APPNAME}\Install ${APPNAME} Service.lnk" "$WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" "-ExecutionPolicy Bypass -File \"$INSTDIR\Service\install-service.ps1\""

        CreateShortCut "$DESKTOP\${APPNAME} - Kiosk.lnk" "$INSTDIR\${APP_EXE}"
        IfFileExists "$INSTDIR\${CONFIG_LAUNCHER_EXE}" 0 no_admin_shortcut_desktop_update
            CreateShortCut "$DESKTOP\${APPNAME} - Admin Config.lnk" "$INSTDIR\${CONFIG_LAUNCHER_EXE}"
            Goto after_admin_shortcut_desktop_update
        no_admin_shortcut_desktop_update:
            CreateShortCut "$DESKTOP\${APPNAME} - Admin Config.lnk" "$INSTDIR\${APP_EXE}" "--config"
        after_admin_shortcut_desktop_update:

        ${If} $STARTUP_CHECKED == 1
            CreateShortCut "$SMSTARTUP\${APPNAME}.lnk" "$INSTDIR\${APP_EXE}"
        ${EndIf}
        ${If} $DESKTOP_CHECKED == 1
            CreateShortCut "$Desktop\${APPNAME}.lnk" "$INSTDIR\${APP_EXE}"
        ${EndIf}

        WriteUninstaller "$INSTDIR\Uninstall.exe"
        WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayName" "${APPNAME}"
        WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
        WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
        WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayVersion" "${VERSION}"
        WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "NoModify" 1
        WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "NoRepair" 1
        Goto end_maint

    do_install:
        SetOutPath "$INSTDIR"
        WriteRegStr HKLM "Software\${APPNAME}" "InstallDir" "$INSTDIR"

        ; Full self-contained publish output (app, runtime deps, locale folders,
        ; plus the merged ConfigLauncher files and Service\ subfolder) in one shot.
        File /r "${PUBLISH_DIR}\*.*"

        SetShellVarContext all
        CreateDirectory "$SMPROGRAMS\${APPNAME}"
        CreateShortCut "$SMPROGRAMS\${APPNAME}\${APPNAME} - Kiosk.lnk" "$INSTDIR\${APP_EXE}"
        IfFileExists "$INSTDIR\${CONFIG_LAUNCHER_EXE}" 0 no_admin_shortcut_programs_install
            CreateShortCut "$SMPROGRAMS\${APPNAME}\${APPNAME} - Admin Config.lnk" "$INSTDIR\${CONFIG_LAUNCHER_EXE}"
            Goto after_admin_shortcut_programs_install
        no_admin_shortcut_programs_install:
            CreateShortCut "$SMPROGRAMS\${APPNAME}\${APPNAME} - Admin Config.lnk" "$INSTDIR\${APP_EXE}" "--config"
        after_admin_shortcut_programs_install:
        CreateShortCut "$SMPROGRAMS\${APPNAME}\Install ${APPNAME} Service.lnk" "$WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" "-ExecutionPolicy Bypass -File \"$INSTDIR\Service\install-service.ps1\""

        CreateShortCut "$DESKTOP\${APPNAME} - Kiosk.lnk" "$INSTDIR\${APP_EXE}"
        IfFileExists "$INSTDIR\${CONFIG_LAUNCHER_EXE}" 0 no_admin_shortcut_desktop_install
            CreateShortCut "$DESKTOP\${APPNAME} - Admin Config.lnk" "$INSTDIR\${CONFIG_LAUNCHER_EXE}"
            Goto after_admin_shortcut_desktop_install
        no_admin_shortcut_desktop_install:
            CreateShortCut "$DESKTOP\${APPNAME} - Admin Config.lnk" "$INSTDIR\${APP_EXE}" "--config"
        after_admin_shortcut_desktop_install:

        ${If} $STARTUP_CHECKED == 1
            CreateShortCut "$SMSTARTUP\${APPNAME}.lnk" "$INSTDIR\${APP_EXE}"
        ${EndIf}
        ${If} $DESKTOP_CHECKED == 1
            CreateShortCut "$Desktop\${APPNAME}.lnk" "$INSTDIR\${APP_EXE}"
        ${EndIf}

        WriteUninstaller "$INSTDIR\Uninstall.exe"
        WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayName" "${APPNAME}"
        WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
        WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
        WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayVersion" "${VERSION}"
        WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "NoModify" 1
        WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "NoRepair" 1

        IfFileExists "$INSTDIR\Service\install-service.ps1" 0 skip_service_install
            nsExec::ExecToLog '"$WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -ExecutionPolicy Bypass -File "$INSTDIR\Service\install-service.ps1"'
        skip_service_install:

    end_maint:
        ; Bring the service back up with the files we just installed/updated.
        nsExec::ExecToLog 'net start "${SERVICE_NAME}"'
SectionEnd

Section "Uninstall"
    IfFileExists "$INSTDIR\Service\uninstall-service.ps1" 0 skip_service_uninstall
        nsExec::ExecToLog '"$WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -ExecutionPolicy Bypass -File "$INSTDIR\Service\uninstall-service.ps1"'
    skip_service_uninstall:

    Delete "$DESKTOP\${APPNAME} - Kiosk.lnk"
    Delete "$DESKTOP\${APPNAME} - Admin Config.lnk"
    Delete "$DESKTOP\${APPNAME}.lnk"

    Delete "$SMPROGRAMS\${APPNAME}\${APPNAME} - Kiosk.lnk"
    Delete "$SMPROGRAMS\${APPNAME}\${APPNAME} - Admin Config.lnk"
    Delete "$SMPROGRAMS\${APPNAME}\Install ${APPNAME} Service.lnk"
    RMDir "$SMPROGRAMS\${APPNAME}"

    Delete "$SMSTARTUP\${APPNAME}.lnk"

    ; Whole tree is now installed via File /r, so remove it the same way.
    RMDir /r "$INSTDIR"

    DeleteRegKey HKLM "Software\${APPNAME}"
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}"
SectionEnd

