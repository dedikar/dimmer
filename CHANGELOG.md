# Changelog

Все заметные изменения этого проекта будут задокументированы в этом файле.
All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.1] - 2026-07-31

### Fixed
- Регулировка яркости теперь работает и с правым Alt (определение Alt через `GetAsyncKeyState` вместо `Control.ModifierKeys`).
- Brightness adjustment now works with the right Alt key too (Alt detection via `GetAsyncKeyState` instead of `Control.ModifierKeys`).

## [0.1.0] - 2026-07-31

### Added
- Регулировка яркости монитора через Alt + колёсико мыши (DDC/CI поверх NvAPI).
- Monitor brightness adjustment via Alt + MouseWheel (DDC/CI over NvAPI).
- Восстановление яркости после выхода из сна (переинициализация I2C).
- Brightness restored after sleep/wake (I2C re-initialization).
- Автозапуск при входе в Windows (меню иконки в трее).
- Autostart on Windows login (tray icon menu).
- Ограничение значения яркости диапазоном монитора (исправление ложных значений после сна).
- Brightness value clamped to the monitor range (fixes bogus values after resume).
- README на русском и английском.
- Bilingual README (RU/EN).
