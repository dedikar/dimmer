# Dimmer

Системный трей-апп для Windows: регулировка яркости внешнего монитора через **Alt + колёсико мыши**.
Работает по DDC/CI поверх NvAPI (нужна видеокарта NVIDIA). Тестировалось: MSI Optix G273, DisplayPort.

System tray app for Windows: adjust external monitor brightness with **Alt + MouseWheel**.
Works via DDC/CI over NvAPI (NVIDIA GPU required). Tested with MSI Optix G273 on DisplayPort.

---

## Возможности / Features

- Alt + колёсико мыши — плавная регулировка яркости / smooth brightness adjustment
- Корректное восстановление яркости после выхода из сна (переинициализация I2C) / brightness restored correctly after sleep/wake (I2C re-initialization)
- Windows 10/11, видеокарта NVIDIA / NVIDIA GPU required

## Использование / Usage

- Зажмите **Alt** и крутите колёсико мыши — яркость изменится / Hold **Alt** and scroll the mouse wheel to change brightness.
- Правый клик по иконке в трее → **Exit** — выход / Right-click the tray icon → **Exit** to quit.
- Один экземпляр приложения / single instance enforced.

## Сборка / Build

Компиляция Roslyn `csc` (VS 2022) / Compile with Roslyn `csc` (VS 2022):

```
csc -nologo -out:dimmer.exe -win32icon:dimmer.ico -target:winexe dimmer.cs
```

## CLI-утилита / CLI tool

`setbright.exe <0-100>` — установка яркости для диагностики / sets brightness directly for diagnostics:

```
csc -nologo -out:setbright.exe setbright.cs
```

## Как это работает / How it works

- Загружает `nvapi64.dll` и использует `NvAPI_I2CWrite` / `NvAPI_I2CRead` для общения по DDC/CI / loads `nvapi64.dll` and uses `NvAPI_I2CWrite` / `NvAPI_I2CRead` to talk DDC/CI.
- Устанавливает VCP-функцию `0x10` (яркость) / sets VCP feature `0x10` (brightness).
- I2C-команды троттлятся: шине монитора нужно ~500 мс между командами / I2C commands are throttled: the monitor bus needs ~500 ms between commands.
- После выхода из сна библиотека драйвера перезагружается для восстановления I2C / on resume from sleep the driver library is reloaded to recover I2C state.

## Дисклеймер / Disclaimer

Управление яркостью по DDC/CI зависит от вашего монитора. Если ваш дисплей не
поддерживает DDC/CI или подключён по неподдерживаемому каналу (например, HDMI/DVI
без DDC), утилита может не работать / DDC/CI brightness control depends on your
monitor. If your display is not DDC/CI-capable over a supported link (e.g.
HDMI/DVI without DDC), this tool may not work.
