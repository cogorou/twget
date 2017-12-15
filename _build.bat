@echo off

@rem ==================================================
@rem ENVIRONMENT VARIABLE

@rem ==================================================
@rem JOB
:JOB

pushd twget
call _build.bat
if ERRORLEVEL 1 goto :EOF
popd

@rem ==================================================
@rem common

