@echo off
setlocal enabledelayedexpansion

echo ========================================
echo GIT SAFE COMMIT
echo ========================================
echo.

REM Check if we're on main branch
for /f "tokens=*" %%i in ('git branch --show-current') do set CURRENT_BRANCH=%%i

if "%CURRENT_BRANCH%"=="main" (
    echo WARNING: You are on MAIN branch!
    echo It's safer to work on a feature branch.
    echo.
    set /p CREATE_BRANCH="Create feature branch now? (y/n): "
    
    if /i "!CREATE_BRANCH!"=="y" (
        set /p BRANCH_NAME="Branch name (e.g., feature/new-feature): "
        git checkout -b !BRANCH_NAME!
        echo Switched to new branch: !BRANCH_NAME!
        echo.
    )
)

echo Current branch: %CURRENT_BRANCH%
echo.

REM Show status
echo Changed files:
git status --short
echo.

REM Commit message
set /p COMMIT_MSG="Commit message: "

if "%COMMIT_MSG%"=="" (
    echo Error: Commit message cannot be empty!
    pause
    exit /b 1
)

REM Add all changes
git add .

REM Show what will be committed
echo.
echo Files to be committed:
git diff --cached --name-status
echo.

set /p CONFIRM="Commit these changes? (y/n): "

if /i "%CONFIRM%"=="y" (
    git commit -m "%COMMIT_MSG%"
    echo.
    echo ========================================
    echo COMMIT SUCCESSFUL!
    echo ========================================
    echo.
    echo To push to GitHub: git push origin %CURRENT_BRANCH%
    echo To undo this commit: git reset --soft HEAD~1
) else (
    git reset
    echo Commit cancelled.
)

echo.
pause
