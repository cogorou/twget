@echo off

@rem ==================================================
@rem ENVIRONMENT VARIABLE

@rem ==================================================
@rem JOB
:JOB

pushd twget
call _clean.bat
popd

@rem ==================================================
@rem common

del /q /s *.user
del /q /s *.aps
del /q /s *.ncb
del /q /s *.log
del /q /s *.suo
del /q /s /AH *.suo
del /q /s BuildLog.htm
