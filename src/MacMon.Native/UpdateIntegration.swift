import AppKit
#if MACMON_UPDATES
import Sparkle

private var updaterController: SPUStandardUpdaterController?
private var closeForUpdate: (@convention(c) () -> Void)?
private var pendingInstall: (() -> Void)?

private final class UpdateDelegate: NSObject, SPUUpdaterDelegate {
    func updater(_ updater: SPUUpdater, shouldPostponeRelaunchForUpdate item: SUAppcastItem,
                 untilInvokingBlock installHandler: @escaping () -> Void) -> Bool {
        guard let close = closeForUpdate else { return false }
        pendingInstall = installHandler
        // Let Avalonia finish saving preferences/history before Sparkle terminates the process.
        DispatchQueue.main.async { close() }
        return true
    }
}
private let updateDelegate = UpdateDelegate()
#endif

@_cdecl("macmon_updates_start")
public func startUpdates(_ close: @escaping @convention(c) () -> Void) -> Int32 {
    #if MACMON_UPDATES
    guard Thread.isMainThread, Bundle.main.bundleURL.pathExtension == "app",
          Bundle.main.bundleIdentifier == "com.galenblakeman.macmon",
          Bundle.main.object(forInfoDictionaryKey: "SUPublicEDKey") is String,
          Bundle.main.object(forInfoDictionaryKey: "SUFeedURL") is String else { return -1 }
    closeForUpdate = close
    if updaterController == nil {
        updaterController = SPUStandardUpdaterController(startingUpdater: true,
            updaterDelegate: updateDelegate, userDriverDelegate: nil)
    }
    return 0
    #else
    return -1
    #endif
}

@_cdecl("macmon_updates_check")
public func checkUpdates() {
    #if MACMON_UPDATES
    if updaterController?.updater.canCheckForUpdates == true { updaterController?.checkForUpdates(nil) }
    #endif
}

@_cdecl("macmon_updates_automatic")
public func automaticUpdates() -> Int32 {
    #if MACMON_UPDATES
    guard let updater = updaterController?.updater else { return -1 }
    return updater.automaticallyChecksForUpdates ? 1 : 0
    #else
    return -1
    #endif
}

@_cdecl("macmon_updates_set_automatic")
public func setAutomaticUpdates(_ enabled: Int32) {
    #if MACMON_UPDATES
    updaterController?.updater.automaticallyChecksForUpdates = enabled != 0
    #endif
}

@_cdecl("macmon_updates_complete_shutdown")
public func completeUpdateShutdown() {
    #if MACMON_UPDATES
    let install = pendingInstall
    pendingInstall = nil
    install?()
    #endif
}
