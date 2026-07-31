# Dimmer

Системный трей-апп для Windows: регулировка яркости внешнего монитора через **Alt + колёсико мыши**. Работает по DDC/CI поверх NvAPI (нужна видеокарта NVIDIA). Тестировалось: MSI Optix G273, DisplayPort.

---

## Русский

### Возможности

- Alt + колёсико мыши — плавная регулировка яркости
- Корректное восстановление яркости после выхода из сна (переинициализация I2C)
- Автозапуск при входе в Windows (включается в меню иконки)
- Windows 10/11, видеокарта NVIDIA

### Использование

- Зажмите **Alt** и крутите колёсико мыши — яркость изменится.
- Правый клик по иконке в трее → **Автозапуск** — включить запуск при входе в Windows.
- Правый клик по иконке в трее → **Exit** — выход.

### Сборка

Компиляция Roslyn `csc` (VS 2022):

```
csc -nologo -out:dimmer.exe -win32icon:dimmer.ico -target:winexe dimmer.cs
```

### CLI-утилита

`setbright.exe <0-100>` — установка яркости для диагностики:

```
csc -nologo -out:setbright.exe setbright.cs
```

### Как это работает

- Загружает `nvapi64.dll` и использует `NvAPI_I2CWrite` / `NvAPI_I2CRead` для общения по DDC/CI.
- Устанавливает VCP-функцию `0x10` (яркость).
- I2C-команды троттлятся: шине монитора нужно ~500 мс между командами.
- После выхода из сна библиотека драйвера перезагружается для восстановления I2C.

### Дисклеймер

Управление яркостью по DDC/CI зависит от вашего монитора. Если ваш дисплей не поддерживает DDC/CI или подключён по неподдерживаемому каналу (например, HDMI/DVI без DDC), утилита может не работать.

---

## English

### Features

- Alt + MouseWheel — smooth brightness adjustment
- Brightness restored correctly after sleep/wake (I2C re-initialization)
- Autostart on Windows login (toggle in the tray icon menu)
- Windows 10/11, NVIDIA GPU required

### Usage

- Hold **Alt** and scroll the mouse wheel to change brightness.
- Right-click the tray icon → **Autostart** to start with Windows.
- Right-click the tray icon → **Exit** to quit.

### Build

Compile with Roslyn `csc` (VS 2022):

```
csc -nologo -out:dimmer.exe -win32icon:dimmer.ico -target:winexe dimmer.cs
```

### CLI tool

`setbright.exe <0-100>` sets brightness directly for diagnostics:

```
csc -nologo -out:setbright.exe setbright.cs
```

### How it works

- Loads `nvapi64.dll` and uses `NvAPI_I2CWrite` / `NvAPI_I2CRead` to talk DDC/CI.
- Sets VCP feature `0x10` (brightness).
- I2C commands are throttled: the monitor bus needs ~500 ms between commands.
- On resume from sleep the driver library is reloaded to recover I2C state.

### Disclaimer

DDC/CI brightness control depends on your monitor. If your display is not DDC/CI-capable over a supported link (e.g. HDMI/DVI without DDC), this tool may not work.
