"""
A scripted stand-in for an OpenAI-compatible model server, used to test WinCompanion's tools end to end.
It reads the latest user message ("TEST <name> ...") and answers with a tool call; after the tool result comes back
it replies with a short text that quotes the result, so the test can see what the tool really did.
"""
import json, sys, threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

SCRATCH = sys.argv[1]            # a folder the tests may create and delete things in
PORT = int(sys.argv[2]) if len(sys.argv) > 2 else 18089
LOG = open(SCRATCH + "\\fake_llm.log", "a", encoding="utf-8")

def call(name, args):
    return {"role": "assistant", "content": "", "tool_calls": [
        {"id": "call_" + name, "type": "function", "function": {"name": name, "arguments": json.dumps(args)}}]}

# test name -> the tool call the "model" makes
SCRIPTS = {
    "windows":    lambda: call("list_windows", {}),
    "snap":       lambda: call("window_action", {"target": "WC Suite Test Window", "action": "snap_left"}),
    "minimize":   lambda: call("window_action", {"target": "WC Suite Test Window", "action": "minimize"}),
    "closewin":   lambda: call("close_window", {"target": "WC Suite Test Window"}),
    "sysinfo":    lambda: call("get_system_info", {}),
    "shot":       lambda: call("save_screenshot", {"folder": SCRATCH, "filename": "shot-test"}),
    "look":       lambda: call("look_at_screen", {"question": "what is on screen?"}),
    "clip":       lambda: call("transform_clipboard", {"action": "shorter"}),
    "ps_safe":    lambda: call("run_powershell", {"command": "Get-Date"}),
    "ps_danger":  lambda: call("run_powershell", {"command": "New-Item -ItemType Directory -Path '" + SCRATCH + "\\made-by-ps'"}),
    "move":       lambda: call("move_or_rename", {"source": SCRATCH + "\\to-move.txt", "destination": SCRATCH + "\\moved.txt"}),
    "recycle":    lambda: call("delete_to_recycle_bin", {"path": SCRATCH + "\\to-delete.txt"}),
    "mkdir":      lambda: call("create_folder", {"path": SCRATCH + "\\new-folder"}),
    "readfile":   lambda: call("read_text_file", {"path": SCRATCH + "\\readme-test.txt"}),
    "recent":     lambda: call("list_recent_files", {"folder": SCRATCH}),
    "note":       lambda: call("add_note", {"text": "fake-llm test note"}),
    "readnotes":  lambda: call("read_notes", {"search": "fake-llm test note"}),
    "reminder":   lambda: call("set_reminder", {"message": "fake test reminder", "minutes": 90}),
    "remind_list":lambda: call("list_reminders", {}),
    "remind_del": lambda: call("cancel_reminder", {"which": "all"}),
    "remember":   lambda: call("remember", {"fact": "The user's test colour is teal"}),
    "memories":   lambda: call("list_memories", {}),
    "forget":     lambda: call("forget", {"text": "teal"}),
    "volume":     lambda: call("change_volume", {"action": "down", "steps": 1}),
    "volume_up":  lambda: call("change_volume", {"action": "up", "steps": 1}),
    "media":      lambda: call("media_control", {"action": "stop"}),
    "theme":      lambda: call("set_theme", {"mode": "toggle"}),
    "weather":    lambda: call("get_weather", {"city": "Chennai"}),
    "kill_prot":  lambda: call("kill_process", {"name": "csrss"}),
    "open":       lambda: call("open_target", {"target": "calc"}),
    "listfiles":  lambda: call("find_files", {"folder": SCRATCH, "pattern": "*.txt"}),
}

class Handler(BaseHTTPRequestHandler):
    def log_message(self, *a): pass

    def _send(self, code, obj):
        body = json.dumps(obj).encode()
        self.send_response(code); self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)

    def _body(self):
        """The request body: either a fixed length, or HTTP chunked encoding (which .NET's HttpClient uses)."""
        if self.headers.get("Transfer-Encoding", "").lower() == "chunked":
            data = b""
            while True:
                size = int(self.rfile.readline().strip().split(b";")[0], 16)
                if size == 0:
                    self.rfile.readline()
                    return data
                data += self.rfile.read(size)
                self.rfile.readline()
        return self.rfile.read(int(self.headers.get("Content-Length", 0)))

    def do_GET(self):                                   # /v1/models (the Detect button)
        self._send(200, {"object": "list", "data": [{"id": "fake-model"}, {"id": "qwen-fake-vl"}]})

    def do_POST(self):
        req = json.loads(self._body())
        msgs = req["messages"]; has_tools = "tools" in req
        LOG.write(f"REQ tools={len(req.get('tools', []))} last_role={msgs[-1]['role']}\n"); LOG.flush()

        if not has_tools:                               # a one-shot request made by a tool (clipboard rewrite, screenshot question)
            content = msgs[-1]["content"]
            text = content if isinstance(content, str) else " ".join(p.get("text", "") for p in content)
            reply = "SHORT TEXT" if "shorter" in text.lower() else "FAKE ANSWER about the image"
            return self._send(200, {"choices": [{"index": 0, "message": {"role": "assistant", "content": reply}, "finish_reason": "stop"}]})

        if msgs[-1]["role"] == "tool":                  # the tool ran; quote what it returned
            first_line = str(msgs[-1]["content"]).splitlines()[0][:160] if msgs[-1]["content"] else ""
            return self._send(200, {"choices": [{"index": 0, "message": {"role": "assistant", "content": f"RESULT: {first_line}"}, "finish_reason": "stop"}]})

        user = next(m["content"] for m in reversed(msgs) if m["role"] == "user")
        name = user.split()[1] if user.upper().startswith("TEST ") and len(user.split()) > 1 else ""
        message = SCRIPTS[name]() if name in SCRIPTS else {"role": "assistant", "content": "UNSCRIPTED: " + user}
        self._send(200, {"choices": [{"index": 0, "message": message, "finish_reason": "tool_calls" if "tool_calls" in message else "stop"}]})

ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
