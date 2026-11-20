"""A stand-in for the Messages API, for CI: answers KScript.AI's requests without a key or network.

Usage: python3 fake-claude.py PORT. It asks for one tool call when the request has tools, answers JSON when the
request has a schema, and plain text otherwise.
"""
import http.server
import json
import sys


class Handler(http.server.BaseHTTPRequestHandler):
    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        messages = body["messages"]
        answered = any(isinstance(m["content"], list) and any(c.get("type") == "tool_result" for c in m["content"]) for m in messages)
        if body.get("tools") and not answered:
            content = [{"type": "tool_use", "id": "toolu_1", "name": body["tools"][0]["name"], "input": {"id": 42}}]
            stop = "tool_use"
        elif body.get("output_config"):
            content = [{"type": "text", "text": json.dumps({"category": "bug", "urgency": 4})}]
            stop = "end_turn"
        else:
            content = [{"type": "text", "text": "tool said " + messages[-1]["content"][0].get("content", "") if answered else "hello from claude"}]
            stop = "end_turn"
        reply = json.dumps({"id": "msg_1", "type": "message", "role": "assistant", "model": body["model"], "content": content,
                            "stop_reason": stop, "stop_sequence": None, "usage": {"input_tokens": 1, "output_tokens": 1}}).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(reply)))
        self.end_headers()
        self.wfile.write(reply)

    def log_message(self, *args):
        pass


http.server.HTTPServer(("127.0.0.1", int(sys.argv[1])), Handler).serve_forever()
