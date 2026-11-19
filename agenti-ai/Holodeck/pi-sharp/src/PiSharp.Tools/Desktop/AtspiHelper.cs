// The desktop toolkit's AT-SPI reader and actor: a python3 + GObject introspection script, written next to the
// process at first use. Needs python3-gi, gir1.2-atspi-2.0 and at-spi2-core, and the session bus of the desktop
// (DBUS_SESSION_BUS_ADDRESS). Avalonia exposes AT-SPI from 12.0 on X11 (11.x registers nothing).
using System.Security.Cryptography;
using System.Text;

namespace PiSharp.Tools.Desktop;

/// <summary>The embedded AT-SPI helper script and where it is written.</summary>
public static class AtspiHelper
{
    /// <summary>Path of the helper, written once per content hash under the temp directory.</summary>
    public static string EnsureWritten()
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Script)))[..12].ToLowerInvariant();
        var path = Path.Combine(Path.GetTempPath(), $"pi-sharp-atspi-{hash}.py");
        if (!File.Exists(path))
        {
            var temp = path + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(temp, Script);
            File.Move(temp, path, overwrite: true);
        }
        return path;
    }

    /// <summary>
    /// inspect TITLE MAX: the window's tree, pruned to what a person sees and can act on, one node per line,
    /// coordinates relative to the window. act TITLE TARGET ACTION [TEXT]: one JSON line with ok, message and
    /// the target's screen centre (for an input fallback). TARGET is a node id from inspect, or role:name / name.
    /// </summary>
    public const string Script = """
        import json, sys, warnings
        warnings.filterwarnings("ignore")
        import gi
        gi.require_version("Atspi", "2.0")
        from gi.repository import Atspi

        SKIP = {"panel", "filler", "unknown", "redundant object", "section", "layered pane", "viewport", "scroll pane", "invalid"}
        SHOWN = ["focused", "checked", "selected", "pressed", "expanded", "collapsed", "indeterminate"]
        ACTIONS = ["click", "press", "activate", "toggle", "jump"]

        def frames():
            desk = Atspi.get_desktop(0)
            for i in range(desk.get_child_count()):
                app = desk.get_child_at_index(i)
                if app is None:
                    continue
                for j in range(app.get_child_count()):
                    w = app.get_child_at_index(j)
                    if w is not None and w.get_role_name() in ("frame", "window", "dialog"):
                        yield w

        def find_window(title):
            t = title.lower()
            found = [w for w in frames() if t in (w.get_name() or "").lower()]
            exact = [w for w in found if (w.get_name() or "").lower() == t]
            pick = exact or found
            if not pick:
                names = sorted({w.get_name() for w in frames() if w.get_name()})
                fail("No accessible window matches '%s'. Accessible windows: %s" % (title, ", ".join(names) or "none"))
            return pick[0]

        def fail(message):
            print(json.dumps({"ok": False, "message": message}))
            sys.exit(3)

        def extents(node):
            try:
                e = node.get_extents(Atspi.CoordType.SCREEN)
                return e.x, e.y, e.width, e.height
            except Exception:
                return None

        def states(node):
            s = node.get_state_set()
            out = [n for n in SHOWN if s.contains(getattr(Atspi.StateType, n.upper()))]
            if not s.contains(Atspi.StateType.ENABLED):
                out.append("disabled")
            return out, s.contains(Atspi.StateType.SHOWING)

        def actions(node):
            try:
                a = node.get_action_iface()
                return [a.get_action_name(i) for i in range(a.get_n_actions())] if a else []
            except Exception:
                return []

        def value(node):
            try:
                t = node.get_text_iface()
                if t is not None:
                    n = t.get_character_count()
                    if n:
                        return t.get_text(0, min(n, 200))
            except Exception:
                pass
            try:
                v = node.get_value_iface()
                if v is not None:
                    return "%g" % v.get_current_value()
            except Exception:
                pass
            return None

        def walk(node, path, depth, visit):
            for i in range(node.get_child_count()):
                child = node.get_child_at_index(i)
                if child is None:
                    continue
                p = path + [i]
                if visit(child, p, depth) and depth < 40:
                    walk(child, p, depth + 1, visit)

        def inspect(title, limit):
            win = find_window(title)
            origin = extents(win) or (0, 0, 0, 0)
            lines = ["[w] %s %r %dx%d" % (win.get_role_name(), win.get_name(), origin[2], origin[3])]
            left = [limit]

            def visit(node, path, depth):
                st, showing = states(node)
                if not showing:
                    return False
                role = node.get_role_name()
                acts = actions(node)
                if role in SKIP and not acts:
                    return True
                if left[0] <= 0:
                    return False
                left[0] -= 1
                e = extents(node)
                where = " @%d,%d %dx%d" % (e[0] - origin[0], e[1] - origin[1], e[2], e[3]) if e else ""
                text = value(node)
                line = "[%s] %s %r%s" % (".".join(map(str, path)), role, node.get_name() or "", where)
                if text is not None and text != node.get_name():
                    line += " value=%r" % text
                if st:
                    line += " " + ",".join(st)
                if acts:
                    line += " actions=" + ",".join(acts)
                lines.append(line)
                return True

            walk(win, [], 0, visit)
            if left[0] <= 0:
                lines.append("[more nodes omitted: raise max]")
            print("\n".join(lines))

        def resolve(win, target):
            if all(c.isdigit() or c == "." for c in target):
                node = win
                for part in target.split(".") if target else []:
                    child = node.get_child_at_index(int(part)) if int(part) < node.get_child_count() else None
                    if child is None:
                        fail("Node %s is gone; inspect again." % target)
                    node = child
                return node
            role, _, name = target.rpartition(":")
            hits = []

            def visit(node, path, depth):
                if not states(node)[1]:
                    return False
                n = (node.get_name() or "").lower()
                if (not role or node.get_role_name() == role.lower()) and name.lower() in (n, (value(node) or "").lower()):
                    hits.append(node)
                return True

            walk(win, [], 0, visit)
            if not hits:
                fail("Nothing visible matches '%s'; inspect the window for ids." % target)
            return hits[0]

        def act(title, target, action, text):
            win = find_window(title)
            node = resolve(win, target)
            e = extents(node)
            centre = [e[0] + e[2] // 2, e[1] + e[3] // 2] if e else None
            label = "%s %r" % (node.get_role_name(), node.get_name() or "")
            out = {"ok": False, "centre": centre, "message": ""}
            if action == "click":
                acts = actions(node)
                pick = next((a for a in ACTIONS if a in acts), acts[0] if acts else None)
                if pick is not None and node.get_action_iface().do_action(acts.index(pick)):
                    out.update(ok=True, message="%s: action %s" % (label, pick))
                else:
                    out["message"] = "%s has no accessible action" % label
            elif action == "focus":
                c = node.get_component_iface()
                out.update(ok=bool(c and c.grab_focus()), message="%s: focus" % label)
            elif action == "set_text":
                ed = node.get_editable_text_iface()
                if ed is not None and ed.set_text_contents(text):
                    out.update(ok=True, message="%s: text set" % label)
                else:
                    c = node.get_component_iface()
                    if c is not None:
                        c.grab_focus()
                    out["message"] = "%s is not accessibly editable" % label
            else:
                fail("Unknown action %s" % action)
            print(json.dumps(out))

        if __name__ == "__main__":
            cmd = sys.argv[1]
            if cmd == "inspect":
                inspect(sys.argv[2], int(sys.argv[3]))
            elif cmd == "act":
                act(sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5] if len(sys.argv) > 5 else "")
            else:
                fail("Unknown command %s" % cmd)
        """;
}
