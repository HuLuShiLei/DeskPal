// DeskPal 的 GNOME Shell 扩展。
// Wayland 下普通程序问不到当前在用哪个窗口，只有桌面自己知道。这个扩展在会话总线上提供一个方法，
// 回答当前窗口的标题、应用和进程号，供本机的 DeskPal 客户端查询。除此之外什么都不做：
// 不联网、不保存、不读窗口里的内容。锁屏时 GNOME 会停用扩展，这个方法也随之消失。
import Gio from 'gi://Gio';
import Shell from 'gi://Shell';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';

const OBJECT_PATH = '/io/github/hulushilei/DeskPal';
const INTERFACE = `
<node>
  <interface name="io.github.hulushilei.DeskPal.Windows">
    <method name="GetFocused">
      <arg type="s" direction="out" name="json"/>
    </method>
    <property name="Version" type="u" access="read"/>
  </interface>
</node>`;

export default class DeskPalExtension extends Extension {
    enable() {
        this._dbus = Gio.DBusExportedObject.wrapJSObject(INTERFACE, this);
        this._dbus.export(Gio.DBus.session, OBJECT_PATH);
    }

    disable() {
        this._dbus?.unexport();
        this._dbus = null;
    }

    // 没有窗口获得焦点时返回 {}。
    GetFocused() {
        const window = global.display.focus_window;
        if (!window)
            return '{}';

        const app = Shell.WindowTracker.get_default().get_window_app(window);
        return JSON.stringify({
            title: window.get_title() ?? '',
            wmClass: window.get_wm_class() ?? '',
            // 没有对应 .desktop 的窗口，GNOME 给的是 window:3 这样的临时编号，不算应用标识。
            appId: app && !app.is_window_backed() ? app.get_id() ?? '' : '',
            appName: app?.get_name() ?? '',
            pid: window.get_pid(),
            fullscreen: window.is_fullscreen(),
        });
    }

    get Version() {
        return 1;
    }
}
