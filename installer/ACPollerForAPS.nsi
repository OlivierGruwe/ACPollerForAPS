; ============================================================================
;  ACPollerForAPS - NSIS installer
;  - Configurable install directory
;  - Optional components: Service / UI (checkable)
;  - Service installed via InstallUtil (reuses ProjectInstaller: registers the
;    service AND creates the Windows Event Log source)
;  - Requires administrator privileges
;
;  Build:  makensis ACPollerForAPS.nsi
;  Place the built binaries under .\dist\ before compiling (see SectionService
;  / SectionUI file lists) and the images under .\assets\ (BMP + ico).
; ============================================================================

Unicode true

;--------------------------------
; Product metadata
;--------------------------------
!define PRODUCT        "ACPollerForAPS"
!define PRODUCT_FULL   "ACPollerForAPS - Invoice pipeline"
!define COMPANY        "Arondor"
!define VERSION        "2.0.0"
!define SERVICE_NAME   "ACPollerForAPS"
!define SVC_EXE        "ACPollerForAPS.Service.exe"
!define UI_EXE         "ACPollerForAPS.UI.exe"

; .NET 4.x InstallUtil (64-bit framework)
!define INSTALLUTIL    "$WINDIR\Microsoft.NET\Framework64\v4.0.30319\InstallUtil.exe"

;--------------------------------
; Includes
;--------------------------------
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"

;--------------------------------
; General
;--------------------------------
Name "${PRODUCT} ${VERSION}"
OutFile "ACPollerForAPS-Setup-${VERSION}.exe"
InstallDir "$PROGRAMFILES64\${PRODUCT}"
InstallDirRegKey HKLM "Software\${COMPANY}\${PRODUCT}" "InstallDir"
RequestExecutionLevel admin      ; service + Event Log => admin required
ShowInstDetails show
ShowUnInstDetails show
BrandingText "${COMPANY} - Activate your content"

;--------------------------------
; MUI2 interface & Arondor visuals
;--------------------------------
!define MUI_ICON                 "assets\acpoller.ico"
!define MUI_UNICON               "assets\acpoller.ico"

; Welcome/Finish side image (164x314 BMP)
!define MUI_WELCOMEFINISHPAGE_BITMAP    "assets\welcome.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP  "assets\welcome.bmp"

; Header image on inner pages (150x57 BMP)
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_BITMAP   "assets\header.bmp"
!define MUI_HEADERIMAGE_RIGHT

!define MUI_ABORTWARNING

;--------------------------------
; Pages
;--------------------------------
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
; Offer to launch the UI at the end
!define MUI_FINISHPAGE_RUN "$INSTDIR\${UI_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Launch the configuration interface"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!insertmacro MUI_PAGE_FINISH

; Uninstaller
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

;--------------------------------
; Optional splash at launch (splash.bmp). Comment out to disable.
;--------------------------------
Function .onInit
  SetOutPath $TEMP
  File /oname=$TEMP\acpoller_splash.bmp "assets\splash.bmp"
  advsplash::show 1500 400 0 -1 $TEMP\acpoller_splash
  Pop $0
  Delete $TEMP\acpoller_splash.bmp
FunctionEnd

;============================================================
; SECTION 1 - Shared core (always installed, hidden-required)
;============================================================
Section "-Core files" SEC_CORE
  SectionIn RO
  SetOutPath "$INSTDIR"

  ; ---------------------------------------------------------------------------
  ; STOP THE SERVICE FIRST — before copying anything. If the service is running,
  ; its exe (and DLLs) are locked and the File copy below would fail. We stop it
  ; here, at the very start, whatever section combination the user picked.
  ; If it isn't installed, these commands are harmless no-ops.
  ; ---------------------------------------------------------------------------
  DetailPrint "Stopping the service (if running)..."
  nsExec::ExecToLog 'sc stop ${SERVICE_NAME}'
  Pop $0
  ; also close the configuration UI if it is open (its exe would be locked too)
  nsExec::ExecToLog 'taskkill /IM "${UI_EXE}" /F'
  Pop $0
  ; give Windows a moment to release the exe/DLL handles
  Sleep 3000

  ; ---------------------------------------------------------------------------
  ; All binaries (service exe, UI exe, Core.dll and every dependency) live
  ; together in the shared build output:  D:\devs\ACPollerForAPS\bin
  ; The .nsi is in  D:\devs\ACPollerForAPS\installer , so bin is  ..\bin .
  ; We ship the WHOLE bin once here — the Service/UI sections then only perform
  ; their specific actions (register the service / create shortcuts).
  ; ---------------------------------------------------------------------------
  ; Ship the WHOLE bin once here, EXCLUDING dev/test artifacts we don't want to
  ; deploy (test config, logs, install logs, debug symbols, the installer itself).
  File /r /x "settings.json" /x "logs" /x "*.InstallLog" /x "*.InstallState" \
             /x "*.pdb" /x "*.xml" "..\bin\*.*"

  ; Persist install dir for the uninstaller / upgrades
  WriteRegStr HKLM "Software\${COMPANY}\${PRODUCT}" "InstallDir" "$INSTDIR"
  WriteRegStr HKLM "Software\${COMPANY}\${PRODUCT}" "Version"    "${VERSION}"

  ; Add/Remove Programs entry
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" \
      "DisplayName" "${PRODUCT_FULL}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" \
      "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" \
      "Publisher" "${COMPANY}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" \
      "DisplayIcon" "$INSTDIR\${UI_EXE}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" \
      "UninstallString" "$INSTDIR\Uninstall.exe"
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" \
      "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" \
      "NoRepair" 1

  WriteUninstaller "$INSTDIR\Uninstall.exe"
SectionEnd

;============================================================
; SECTION 2 - Windows service (optional, checked by default)
;============================================================
Section "Service (Windows background service)" SEC_SVC
  SetOutPath "$INSTDIR"

  ; Files are already installed by the Core section (whole bin). This section
  ; only registers and starts the Windows service.

  ; providers/ folder for ERP plugins (ship empty; DLLs are dropped here later)
  CreateDirectory "$INSTDIR\providers"

  ; Deploy a default settings.json ONLY if none exists yet, so the service can
  ; start on a fresh machine (and an existing config is never overwritten).
  IfFileExists "$INSTDIR\settings.json" +2 0
    File "/oname=$INSTDIR\settings.json" "assets\settings.default.json"

  ; --- Pre-flight: verify the service exe and core dependency are present ----
  IfFileExists "$INSTDIR\${SVC_EXE}" +2 0
    MessageBox MB_ICONSTOP "Missing ${SVC_EXE} in $INSTDIR.$\r$\nCheck that ..\bin contains the built service."
  IfFileExists "$INSTDIR\ACPollerForAPS.Core.dll" +2 0
    MessageBox MB_ICONSTOP "Missing ACPollerForAPS.Core.dll in $INSTDIR."

  ; --- If a service with the same name already exists, stop & remove it ------
  DetailPrint "Checking for an existing service..."
  nsExec::ExecToLog 'sc query ${SERVICE_NAME}'
  Pop $0
  ${If} $0 == 0
    DetailPrint "Existing service found - stopping and removing it..."
    nsExec::ExecToLog 'sc stop ${SERVICE_NAME}'
    Pop $0
    Sleep 2000
    nsExec::ExecToLog '"${INSTALLUTIL}" /u "$INSTDIR\${SVC_EXE}"'
    Pop $0
    nsExec::ExecToLog 'sc delete ${SERVICE_NAME}'
    Pop $0
    Sleep 1500
  ${EndIf}

  ; --- Install the service (ProjectInstaller also handles the Event Log source)
  DetailPrint "Installing the Windows service..."
  nsExec::ExecToLog '"${INSTALLUTIL}" "$INSTDIR\${SVC_EXE}"'
  Pop $0
  ${If} $0 != 0
    DetailPrint "InstallUtil returned $0 - see $INSTDIR\${SVC_EXE}.InstallLog"
    MessageBox MB_ICONEXCLAMATION "Service installation failed (code $0).$\r$\n$\r$\n\
      Detailed reason in:$\r$\n$INSTDIR\${SVC_EXE}.InstallLog$\r$\n$\r$\n\
      Common causes: leftover Event Log source, missing dependency DLL, or not running as administrator."
    Goto svc_done
  ${EndIf}

  DetailPrint "Starting the service..."
  nsExec::ExecToLog 'sc start ${SERVICE_NAME}'
  Pop $0
  ${If} $0 != 0
    MessageBox MB_ICONEXCLAMATION "The service was installed but did not start (code $0).$\r$\n\
      Check the logs\ folder and the ACPollerForAPS Windows Event Log."
  ${EndIf}

  svc_done:
SectionEnd

;============================================================
; SECTION 3 - Configuration UI (optional, checked by default)
;============================================================
Section "Configuration interface (UI)" SEC_UI
  SetOutPath "$INSTDIR"

  ; Files are already installed by the Core section (whole bin). This section
  ; only creates the Start Menu shortcuts for the UI.

  IfFileExists "$INSTDIR\${UI_EXE}" +2 0
    MessageBox MB_ICONSTOP "Missing ${UI_EXE} in $INSTDIR.$\r$\nCheck that ..\bin contains the built UI."

  CreateDirectory "$SMPROGRAMS\${PRODUCT}"
  CreateShortCut "$SMPROGRAMS\${PRODUCT}\${PRODUCT} Configuration.lnk" \
      "$INSTDIR\${UI_EXE}" "" "$INSTDIR\${UI_EXE}" 0
  CreateShortCut "$SMPROGRAMS\${PRODUCT}\Uninstall ${PRODUCT}.lnk" \
      "$INSTDIR\Uninstall.exe"
SectionEnd

;--------------------------------
; Component descriptions
;--------------------------------
!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_SVC} \
    "The Windows background service that runs the pipeline (routing, mapping, delivery). Install on the server."
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_UI} \
    "The configuration interface to edit settings.json. Install on the server or on an admin workstation."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

;============================================================
; UNINSTALLER
;============================================================
Section "Uninstall"
  ; Stop & remove the service (ProjectInstaller also removes the Event Log source)
  DetailPrint "Stopping the service..."
  nsExec::ExecToLog 'sc stop ${SERVICE_NAME}'
  Pop $0
  ; close the configuration UI if open (its exe/DLLs would be locked)
  nsExec::ExecToLog 'taskkill /IM "${UI_EXE}" /F'
  Pop $0
  Sleep 3000
  DetailPrint "Uninstalling the service..."
  nsExec::ExecToLog '"${INSTALLUTIL}" /u "$INSTDIR\${SVC_EXE}"'
  Pop $0
  Sleep 1500

  ; Remove installed files. We ship whole Release folders, so delete the known
  ; artifacts and DLLs, but PRESERVE logs\, providers\ and settings.json.
  Delete "$INSTDIR\*.exe"
  Delete "$INSTDIR\*.dll"
  Delete "$INSTDIR\*.config"
  Delete "$INSTDIR\*.InstallLog"
  Delete "$INSTDIR\*.InstallState"
  Delete "$INSTDIR\Uninstall.exe"

  ; NOTE: logs\, providers\ and settings.json are deliberately kept, to preserve
  ; configuration and history. Remove them manually for a full wipe.

  ; Shortcuts
  Delete "$SMPROGRAMS\${PRODUCT}\${PRODUCT} Configuration.lnk"
  Delete "$SMPROGRAMS\${PRODUCT}\Uninstall ${PRODUCT}.lnk"
  RMDir  "$SMPROGRAMS\${PRODUCT}"

  ; Registry
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}"
  DeleteRegKey HKLM "Software\${COMPANY}\${PRODUCT}"

  ; Remove install dir only if empty (keeps logs/config if present)
  RMDir "$INSTDIR"
SectionEnd
