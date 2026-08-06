@echo off
cd /d D:\ChuanQi\Kmyq\Crystal-master
powershell -ExecutionPolicy Bypass -File "Build\net-dualopen.ps1" -LoginId probe1 -LoginPw probe1 -CharName probe -BLoginId probe2 -BLoginPw probe2 -BCharName probe2b -SoakMs 5760000 -TimeoutMs 7400000 > "Build\g4-soak.log" 2>&1
echo EXITCODE=%ERRORLEVEL% >> "Build\g4-soak.log"
