# Avalonia UI Visual Designer — архитектура Visual Studio PoC

## Назначение

Этот Proof of Concept подтверждает, что один экземпляр Designer может использоваться как самостоятельное Avalonia-приложение и как Designer, подключённый к Visual Studio, без загрузки Avalonia или Eremex в `devenv.exe` и без второй реализации Canvas, Toolbox либо Property Inspector.

Поддерживаемый AXAML subset намеренно минимален: `Window`/`UserControl`, один `Canvas`, `Button` и `TextBox`. Поддерживаемые изменения: `Content`/`Text`, `Width`, `Height`, `Canvas.Left`, `Canvas.Top`.

## Проекты и границы

```text
AvaloniaDesigner.VSIX (net472)
    -> AvaloniaDesigner.Host.Protocol
    -> Visual Studio command, text buffer, process lifecycle

AvaloniaDesigner.VsHost (net6.0)
    -> AvaloniaDesigner.Host.Protocol
    -> FormDesigner
        -> MainWindow -> shared DesignerSurface
        -> DesignerDocumentSession
        -> AxamlImportService / AxamlPatchWriter
```

| Проект | Ответственность | Запрещённые зависимости |
|---|---|---|
| `AvaloniaDesigner.Host.Protocol` | DTO, checksum, версионированные сообщения и Named Pipes transport. | Avalonia, VSSDK, Eremex, `DesignerSurface`. |
| `AvaloniaDesigner.VsHost` | Отдельный Avalonia process, импорт snapshot, отображение общего Designer и создание patch. | Visual Studio SDK. |
| `AvaloniaDesigner.VSIX` | Команда Visual Studio, запуск/reuse host, IPC и применение edits к text buffer. | Avalonia, Eremex, `FormDesigner`, visual plugins. |

`AvaloniaDesigner.VSIX.csproj` имеет ProjectReference только на `AvaloniaDesigner.Host.Protocol`. `AvaloniaDesigner.VsHost.exe` попадает в VSIX как content, а не как assembly reference, поэтому Visual Studio не загружает Avalonia, Eremex или plugin assemblies.

## Визуальная реализация

`VsHostWindow` наследует существующий `FormDesigner.Views.MainWindow`; это временный host interaction controller, а не вторая дизайнерская реализация. Внутри него находится уже существующий `DesignerSurface`, содержащий Canvas, Toolbox и Property Inspector. Копий `VsDesignerSurface`, VS Toolbox или VS Property Inspector в PoC нет.

Причина этого переходного решения: в текущем standalone-коде часть проверенной pointer/drag/resize логики ещё делегируется из `DesignerSurface` в `MainWindow`. Переносить её заново в рамках IPC PoC было бы рискованной архитектурной подменой. Следующий этап после PoC должен завершить перенос interaction controller в `DesignerSurface`; тогда `VsHostWindow` сможет использовать только минимальный host chrome. Это ограничение не меняет ownership Canvas и не создаёт второй UI.

## Protocol

Transport — локальные Named Pipes с случайным именем, содержащим PID Visual Studio и GUID. TCP listener не используется.

PoC предполагает один доверенный локальный user session. Случайное имя pipe исключает коллизии между экземплярами Visual Studio, но не является заменой ACL/секретного handshake для будущего multi-user или elevated-host сценария.

```text
Visual Studio
    -> Hello
VsHost
    -> HelloAck
Visual Studio
    -> OpenDocument(snapshot)
VsHost
    -> DocumentOpened(capability report)
VsHost
    -> ApplyDesignerPatch(AxamlTextEdit[])
Visual Studio
    -> PatchApplied | Error(SOURCE_VERSION_CONFLICT)
```

Envelope всех сообщений содержит `ProtocolVersion`, `RequestId`, `DocumentId`, `MessageType` и сериализованный XML payload. Версия протокола сейчас равна `1`. Сообщения `DocumentChanged`, `ReloadDocument`, `CloseDocument`, `HostShutdown` и `Error` уже определены, чтобы не менять wire contract при следующем шаге live synchronization.

## Владение документом и patch flow

Visual Studio остаётся единственным владельцем открытого документа.

```text
Visual Studio text buffer snapshot
    -> OpenDocumentPayload(text, version, checksum)
    -> VsHost / AxamlImportService
    -> DesignerDocumentSession / shared DesignerSurface
    -> AxamlPatchWriter
    -> ApplyDesignerPatch(AxamlTextEdit[])
    -> VSIX / EnvDTE TextDocument buffer
    -> Visual Studio dirty document
    -> Ctrl+S / Save All
```

`VsHost` не вызывает `File.WriteAllText` для исходного AXAML. `VsDocumentBuffer` применяет text edits в обратном порядке только к существующему text buffer. Поэтому обычные команды `Ctrl+S`, `Save` и `Save All` Visual Studio продолжают управлять дисковым файлом.

Используется существующий `AxamlPatchWriter`, а не отдельный serializer. Изменение `Width="160"` на `Width="220"` заменяет только span значения `160`. Комментарии, namespaces, порядок элементов и неизвестные attributes остаются частью исходного текста.

## Версии и конфликты

`OpenDocumentPayload` содержит snapshot `Version` и SHA-256 `Checksum`. Перед применением patch VSIX сверяет их с актуальным text buffer. Несовпадение блокирует patch и возвращает `SOURCE_VERSION_CONFLICT`; host показывает предложение перезагрузить документ. Patch поверх текста, изменённого пользователем после открытия Designer, не применяется молча.

Для PoC предусмотрена явная кнопка `Apply changes`. `Reload from Visual Studio` передаёт новый snapshot по `ReloadDocument`. Непрерывная синхронизация на каждое нажатие клавиши и трехсторонний merge не входят в этот этап.

## Crash isolation

`AvaloniaDesigner.VsHost.exe` является отдельным process. Если он завершается или Named Pipe закрывается, VSIX освобождает connection, завершает ожидающие requests ошибкой и фиксирует `VSIX_HOST_DISCONNECTED`. Visual Studio не содержит Avalonia/Eremex visual code и продолжает работать. Следующее открытие команды создаст новый host process.

Для PoC отсутствуют автоматическое восстановление несохранённых изменений host и автоматическое применение recovery snapshot. Несохранённые изменения остаются только в host до `Apply changes`.

## Diagnostics

VSIX пишет события `VSIX_DESIGNER_COMMAND`, `VSIX_HOST_START`, `VSIX_HOST_READY`, `VSIX_IPC_CONNECTED`, `VSIX_OPEN_DOCUMENT`, `VSIX_PATCH_RECEIVED`, `VSIX_PATCH_APPLIED`, `VSIX_VERSION_CONFLICT` и `VSIX_HOST_DISCONNECTED` в Output pane `Avalonia UI Visual Designer` и diagnostics trace.

`VsHost` пишет `VSHOST_START`, `VSHOST_PROTOCOL_HANDSHAKE`, `VSHOST_DOCUMENT_RECEIVED`, `VSHOST_AXAML_IMPORT_START`, `VSHOST_AXAML_IMPORT_SUCCESS`, `VSHOST_SURFACE_ATTACHED`, `VSHOST_PATCH_CREATE_START`, `VSHOST_PATCH_SENT`, `VSHOST_DOCUMENT_RELOAD` и `VSHOST_DISCONNECTED`.

## Ограничения PoC

Не реализованы native embedded Designer tab, контекстное `Open With`, native Visual Studio Toolbox/Properties, split view, VS Undo/Redo, Grid/StackPanel/DataGrid/Eremex round-trip, SQL/DLL, изменение NuGet package references и live synchronization. Команда всегда видна в `Tools -> Avalonia UI Visual Designer -> Open Designer`; при запуске она проверяет, что активный документ имеет расширение `.axaml`.

`Eremex` намеренно не загружается в `VsHost` PoC: проверяется документное владение, IPC и общий Designer, а не third-party visual stack.

## Проверка вручную

Fixture: [Samples/VisualStudioPoC/SimpleAvaloniaApp/MainWindow.axaml](../Samples/VisualStudioPoC/SimpleAvaloniaApp/MainWindow.axaml).

1. Откройте `SimpleAvaloniaApp.csproj` в Visual Studio.
2. Откройте `MainWindow.axaml` и выберите `Tools -> Avalonia UI Visual Designer -> Open Designer`.
3. Измените `Button1.Content`, `Button1.Width` и положение `Button1` на Canvas.
4. Нажмите `Apply changes` в отдельном `AvaloniaDesigner.VsHost.exe`.
5. Убедитесь, что AXAML buffer в Visual Studio стал dirty и diff содержит только изменённые attribute values.
6. Проверьте сохранность `<!-- keep me -->` и `custom:Custom.Unknown="keep-me"`.
7. Измените AXAML вручную в Visual Studio до Apply: host должен отклонить устаревший patch и запросить Reload.
8. Завершите `AvaloniaDesigner.VsHost.exe`: Visual Studio должна остаться работоспособной.

## Проверки

`FormDesigner.ExportSmokeTests` содержит: `VsHostProtocolHandshakeWorks`, `VsHostCanStartAndAcceptConnection`, `VsHostUsesSharedDesignerSurface`, `VsHostReturnsMinimalAxamlPatch`, `VsHostRoundTripPreservesComment`, `VsHostRoundTripPreservesUnknownAttribute`, `VsHostRejectsPatchForStaleDocumentVersion` и `BridgeSurvivesVsHostDisconnect`.

Полная проверка применения в Visual Studio text buffer — ручная, потому что автоматизированный запуск `devenv.exe` в обычном smoke runner нестабилен и не нужен для минимального архитектурного решения.
