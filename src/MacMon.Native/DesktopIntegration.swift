import AppKit
import Carbon
import ServiceManagement
import UserNotifications

private func packaged() -> Bool {
    Bundle.main.bundleIdentifier == "com.galenblakeman.macmon" && Bundle.main.bundleURL.pathExtension == "app"
}

@_cdecl("macmon_login_status")
public func loginStatus() -> Int32 {
    guard packaged() else { return -1 }
    return Int32(SMAppService.mainApp.status.rawValue)
}

@_cdecl("macmon_set_login")
public func setLogin(_ enabled: Int32) -> Int32 {
    guard packaged() else { return -1 }
    do {
        if enabled != 0 { try SMAppService.mainApp.register() }
        else { try SMAppService.mainApp.unregister() }
        return Int32(SMAppService.mainApp.status.rawValue)
    } catch { return -2 }
}

@_cdecl("macmon_open_login_settings")
public func openLoginSettings() { if packaged() { SMAppService.openSystemSettingsLoginItems() } }

private var hotkey: EventHotKeyRef?
private var handler: EventHandlerRef?
private var keyCallback: (@convention(c) () -> Void)?
private var currentKey: UInt32 = UInt32.max
private var currentModifiers: UInt32 = 0

@_cdecl("macmon_set_hotkey")
public func setHotkey(_ key: UInt32, _ modifiers: UInt32, _ callback: @escaping @convention(c) () -> Void) -> Int32 {
    guard packaged() else { return -1 }
    if key == UInt32.max {
        if let key = hotkey { UnregisterEventHotKey(key) }
        hotkey = nil; keyCallback = nil; currentKey = UInt32.max; return 0
    }
    if key == currentKey && modifiers == currentModifiers { keyCallback = callback; return 0 }
    if handler == nil {
        var type = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        let status = InstallEventHandler(GetApplicationEventTarget(), { _, event, _ in
            guard let event else { return OSStatus(eventNotHandledErr) }
            var id = EventHotKeyID()
            guard GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID), nil,
                                    MemoryLayout<EventHotKeyID>.size, nil, &id) == noErr,
                  id.signature == 0x4d41434d && id.id == 1 else { return OSStatus(eventNotHandledErr) }
            keyCallback?(); return noErr
        }, 1, &type, nil, &handler)
        guard status == noErr else { return status }
    }
    var replacement: EventHotKeyRef?
    let status = RegisterEventHotKey(key, modifiers, EventHotKeyID(signature: 0x4d41434d, id: 1), GetApplicationEventTarget(), 0, &replacement)
    guard status == noErr else { return status }
    if let old = hotkey { UnregisterEventHotKey(old) }
    hotkey = replacement; keyCallback = callback; currentKey = key; currentModifiers = modifiers
    return 0
}

@_cdecl("macmon_desktop_dispose")
public func disposeDesktop() {
    if let key = hotkey { UnregisterEventHotKey(key) }
    if let eventHandler = handler { RemoveEventHandler(eventHandler) }
    hotkey = nil; handler = nil; keyCallback = nil; currentKey = UInt32.max
}

@_cdecl("macmon_activate")
public func activate() { NSApplication.shared.activate(ignoringOtherApps: true) }

private final class CaptureNotifications: NSObject, UNUserNotificationCenterDelegate {
    func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification,
                                withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner, .sound])
    }
}
private let notifications = CaptureNotifications()

@_cdecl("macmon_request_notifications")
public func requestNotifications(_ callback: @escaping @convention(c) (Int32) -> Void) {
    guard packaged() else { callback(-1); return }
    let center = UNUserNotificationCenter.current()
    center.delegate = notifications
    center.requestAuthorization(options: [.alert, .sound]) { allowed, _ in callback(allowed ? 1 : 0) }
}

@_cdecl("macmon_notify_capture")
public func notifyCapture(_ title: UnsafePointer<CChar>, _ message: UnsafePointer<CChar>) {
    guard packaged() else { return }
    let content = UNMutableNotificationContent()
    content.title = String(cString: title); content.body = String(cString: message); content.sound = .default
    let center = UNUserNotificationCenter.current()
    center.delegate = notifications
    center.getNotificationSettings { settings in
        guard settings.authorizationStatus == .authorized || settings.authorizationStatus == .provisional else { return }
        center.add(UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil))
    }
}
