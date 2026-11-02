@echo off
chcp 65001 >nul
title ExtraScreen - Cài đặt ứng dụng lên Điện thoại Android
echo ======================================================================
echo           ExtraScreen - Cài đặt ứng dụng lên Điện thoại
echo ======================================================================
echo.
echo [1/3] Đang tìm kiếm thiết bị Android kết nối qua cáp USB...
cd /d "%~dp0"
adb\adb.exe devices
echo.

adb\adb.exe get-state >nul 2>&1
if errorlevel 1 (
    echo [LƯU Ý] Không phát hiện thiết bị hoặc thiết bị chưa cấp quyền!
    echo - Hãy kiểm tra cáp USB đã cắm chắc chắn chưa.
    echo - Mở màn hình điện thoại và bấm "Cho phép gỡ lỗi USB".
    echo.
    pause
    exit /b 1
)

echo [2/3] Đang cài đặt tệp ExtraScreen-v1.0.0.apk...
adb\adb.exe install -r ExtraScreen-v1.0.0.apk
if errorlevel 1 (
    echo [LỖI] Cài đặt thất bại! Hãy kiểm tra dung lượng máy hoặc quyền gỡ lỗi USB.
    echo.
    pause
    exit /b 1
)

echo.
echo [3/3] Cài đặt thành công! Đang khởi động ứng dụng trên điện thoại...
adb\adb.exe shell am start -n com.extrascreen/.stream.StreamActivity
echo.
echo ======================================================================
echo HOÀN TẤT! Điện thoại đã sẵn sàng làm màn hình phụ.
echo Hãy mở phần mềm ExtraScreen trên máy tính PC để bắt đầu sử dụng.
echo ======================================================================
echo.
pause
