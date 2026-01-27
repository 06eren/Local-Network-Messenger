; Local Network Messenger - NSIS installer script
; Build with:
;   makensis /DAPP_VERSION=1.0.0 /DAPP_BUILD_DIR="..\\Local-Network-Messenger\\bin\\Release\\net10.0-windows\\win-x64\\publish" Installer\\LocalNetworkMessenger.nsi

!include "MUI2.nsh"

!define APP_NAME "Local Network Messenger"
!define APP_EXE "Local-Network-Messenger.exe"
!define APP_PUBLISH_DIR "..\\Local-Network-Messenger\\bin\\Release\\net10.0-windows\\win-x64\\publish"
!ifndef APP_VERSION
!define APP_VERSION "0.1.0"
!endif
!ifndef APP_BUILD_DIR
!define APP_BUILD_DIR "${APP_PUBLISH_DIR}"
!endif

Var StartMenuFolder

Name "${APP_NAME}"
OutFile "..\\dist\\${APP_NAME}-Setup-${APP_VERSION}.exe"
InstallDir "$PROGRAMFILES64\\${APP_NAME}"
InstallDirRegKey HKLM "Software\\${APP_NAME}" "InstallDir"
RequestExecutionLevel admin
ShowInstDetails show
ShowUninstDetails show

!define MUI_ABORTWARNING
!define MUI_UNABORTWARNING
!define MUI_STARTMENUPAGE_DEFAULTFOLDER "${APP_NAME}"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\\LICENSE.txt"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_STARTMENU Application $StartMenuFolder
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "Turkish"

Section "MainSection" SEC01
  SetOutPath "$INSTDIR"
  File /r /x "*.pdb" /x "*.dbg" "${APP_BUILD_DIR}\\*.*"

  WriteUninstaller "$INSTDIR\\Uninstall.exe"

  WriteRegStr HKLM "Software\\${APP_NAME}" "InstallDir" "$INSTDIR"
  WriteRegStr HKLM "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKLM "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKLM "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}" "UninstallString" "$INSTDIR\\Uninstall.exe"
  WriteRegStr HKLM "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}" "DisplayIcon" "$INSTDIR\\${APP_EXE}"

  !insertmacro MUI_STARTMENU_WRITE_BEGIN Application
    CreateDirectory "$SMPROGRAMS\\$StartMenuFolder"
    CreateShortcut "$SMPROGRAMS\\$StartMenuFolder\\${APP_NAME}.lnk" "$INSTDIR\\${APP_EXE}"
    CreateShortcut "$SMPROGRAMS\\$StartMenuFolder\\Uninstall.lnk" "$INSTDIR\\Uninstall.exe"
  !insertmacro MUI_STARTMENU_WRITE_END

  CreateShortcut "$DESKTOP\\${APP_NAME}.lnk" "$INSTDIR\\${APP_EXE}"
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\\${APP_NAME}.lnk"
  !insertmacro MUI_STARTMENU_GETFOLDER Application $StartMenuFolder
  Delete "$SMPROGRAMS\\$StartMenuFolder\\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\\$StartMenuFolder\\Uninstall.lnk"
  RMDir "$SMPROGRAMS\\$StartMenuFolder"

  RMDir /r "$INSTDIR"

  DeleteRegKey HKLM "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}"
  DeleteRegKey HKLM "Software\\${APP_NAME}"
SectionEnd
