rmdir /s /q .\nupkg\
mkdir .\nupkg\
dotnet build -c Release
call uninstall_global.bat
call install_global.bat
