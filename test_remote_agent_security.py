import contextlib
import io
import json
import pathlib
import unittest
from unittest import mock


def load_agent_namespace():
    source_path = pathlib.Path(__file__).with_name("RemoteAgentProgram.py")
    source = source_path.read_text(encoding="utf-8")
    marker = "\nwhile True:\n    raw_line = read_request_line()"
    entrypoint = source.rfind(marker)
    if entrypoint < 0:
        raise RuntimeError("Remote agent protocol loop marker was not found")
    namespace = {"__name__": "remote_agent_security_test"}
    exec(compile(source[:entrypoint], str(source_path), "exec"), namespace)
    return namespace


class RemoteAgentSecurityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.agent = load_agent_namespace()

    def test_inventory_uses_cli_without_raw_gateway_client(self):
        agent = self.agent
        calls = []
        agent["model_config_source_hash"] = lambda: "test-config"
        agent["openclaw_json"] = lambda args: calls.append(args) or {}
        agent["MODEL_CACHE"] = None
        agent["MODEL_CACHE_SOURCE_HASH"] = None
        result = agent["models_inventory_core"](force=True)
        self.assertTrue(result["ok"])
        self.assertEqual(calls, [["models", "status", "--json"], ["models", "list", "--json"]])
        self.assertNotIn("gateway_settings", agent)
        self.assertNotIn("gateway_model_list", agent)

    def test_http_model_add_is_rejected_before_key_storage(self):
        agent = self.agent
        agent["P"] = {"modelRef": "provider/model", "baseUrl": "http://provider.example/v1", "apiKey": "test-key"}
        commands = []
        agent["openclaw_command"] = lambda *args, **kwargs: commands.append(args)
        with contextlib.redirect_stdout(io.StringIO()), self.assertRaises(SystemExit):
            agent["models_add"]()
        self.assertEqual(commands, [])

    def test_latency_refuses_http_before_network_or_cli(self):
        agent = self.agent
        agent["MODEL_PROVIDER_CACHE"] = {
            "provider": {"api": "openai-completions", "baseUrl": "http://provider.example/v1", "apiKey": "test-key"}
        }
        agent["MODEL_PROVIDER_CACHE_AT"] = agent["time"].monotonic()
        agent["P"] = {"models": ["provider/model"]}
        output = io.StringIO()
        with mock.patch.object(agent["urllib"].request, "build_opener") as opener, \
                mock.patch.object(agent["subprocess"], "run") as run, \
                contextlib.redirect_stdout(output):
            agent["models_test_latency"]()
        result = json.loads(output.getvalue())["results"][0]
        self.assertFalse(result["success"])
        self.assertIn("HTTPS", result["error"])
        opener.assert_not_called()
        run.assert_not_called()
        self.assertNotIn("gateway_model_probe", agent)

    def test_provider_redirects_cannot_downgrade_or_change_origin(self):
        agent = self.agent
        handler = agent["SameOriginHttpsRedirectHandler"]()
        request = agent["urllib"].request.Request(
            "https://provider.example/v1/chat/completions",
            data=b"{}",
            headers={"Authorization": "Bearer test-key"},
        )
        for target in ("http://provider.example/v1/", "https://other.example/v1/"):
            with self.subTest(target=target), self.assertRaises(agent["urllib"].error.HTTPError) as caught:
                handler.redirect_request(request, io.BytesIO(), 307, "Temporary Redirect", {}, target)
            caught.exception.close()
        get_request = agent["urllib"].request.Request(
            "https://provider.example/v1/", headers={"Authorization": "Bearer test-key"}
        )
        redirected = handler.redirect_request(
            get_request, io.BytesIO(), 302, "Temporary Redirect", {}, "https://provider.example/v2/"
        )
        self.assertIn("Authorization", redirected.headers)
        ipv6_request = agent["urllib"].request.Request(
            "https://[2001:db8::1]/v1/", headers={"Authorization": "Bearer test-key"}
        )
        ipv6_target = "https://[2001:0db8:0:0:0:0:0:1]/v2/"
        ipv6_redirect = handler.redirect_request(
            ipv6_request, io.BytesIO(), 302, "Temporary Redirect", {}, ipv6_target
        )
        self.assertEqual(ipv6_redirect.full_url, ipv6_target)

    def test_https_parser_rejects_alternate_unsafe_forms(self):
        validate = self.agent["require_https_base_url"]
        self.assertEqual(validate("HTTPS://provider.example/v1/"), "HTTPS://provider.example/v1")
        for value in ("http://provider.example/v1", "https://user@provider.example", "https://provider.example:bad"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                validate(value)


if __name__ == "__main__":
    unittest.main()
