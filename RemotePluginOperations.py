PLUGIN_QUERY_TIMEOUT_SECONDS = 90
PLUGIN_LIFECYCLE_TIMEOUT_SECONDS = 900

def require_plugin_id(value):
    if not isinstance(value, str) or not re.fullmatch(r"[A-Za-z0-9@][A-Za-z0-9@._/-]*", value):
        raise ValueError("插件 ID 格式无效")
    return value

def require_plugin_spec(value):
    if not isinstance(value, str) or not value.strip():
        raise ValueError("请输入插件包来源")
    if value.startswith("-") or any(ord(character) < 32 for character in value):
        raise ValueError("插件来源不能以连字符开头或包含控制字符")
    return value

def plugin_json_command(arguments, timeout=PLUGIN_QUERY_TIMEOUT_SECONDS):
    try:
        completed = subprocess.run(
            ["openclaw", "plugins", *arguments],
            capture_output=True,
            text=True,
            timeout=timeout,
            env=os.environ.copy())
    except FileNotFoundError as error:
        raise RuntimeError("服务器找不到 openclaw 命令，请确认 OpenClaw 已安装") from error
    except subprocess.TimeoutExpired as error:
        raise TimeoutError("OpenClaw 插件命令超时") from error
    stdout = completed.stdout or ""
    stderr = redact_cli_error(completed.stderr or "")
    if completed.returncode != 0:
        detail = stderr or redact_cli_error(stdout)
        raise RuntimeError("OpenClaw 插件命令失败" + (": " + detail if detail else ""))
    try:
        result = json.loads(stdout)
    except json.JSONDecodeError as error:
        raise ValueError("OpenClaw 插件命令没有返回有效 JSON：" + redact_cli_error(stdout)) from error
    response = {"ok": True, "result": result}
    if stderr.strip():
        response["diagnostics"] = stderr.strip()
    return response

def plugin_mutation_command(arguments, accept_capabilities=False, timeout=PLUGIN_LIFECYCLE_TIMEOUT_SECONDS):
    command = ["openclaw", "plugins", *arguments]
    if accept_capabilities:
        command.append("--accept-capabilities")
    try:
        completed = subprocess.run(
            command,
            capture_output=True,
            text=True,
            timeout=timeout,
            env=os.environ.copy())
    except FileNotFoundError as error:
        raise RuntimeError("服务器找不到 openclaw 命令，请确认 OpenClaw 已安装") from error
    except subprocess.TimeoutExpired as error:
        raise TimeoutError("OpenClaw 插件操作超时") from error
    stdout = (completed.stdout or "").strip()
    stderr = redact_cli_error(completed.stderr or "").strip()
    if completed.returncode != 0:
        detail = stderr or redact_cli_error(stdout)
        raise RuntimeError("OpenClaw 插件操作失败" + (": " + detail if detail else ""))
    response = {"ok": True, "message": stdout}
    if stderr:
        response["diagnostics"] = stderr
    return response

def plugins_inventory():
    return plugin_json_command(["list", "--json"])

def plugins_search():
    query = P.get("query")
    if not isinstance(query, str) or not query.strip():
        raise ValueError("请输入插件搜索词")
    query = query.strip()
    if query.startswith("-"):
        raise ValueError("搜索词不能以连字符开头")
    return plugin_json_command(["search", query, "--json"])

def plugins_inspect():
    plugin_id = require_plugin_id(P.get("pluginId"))
    return plugin_json_command(["inspect", plugin_id, "--json"])

def plugins_enable():
    plugin_id = require_plugin_id(P.get("pluginId"))
    accepted = P.get("acceptCapabilities") is True
    return plugin_mutation_command(["enable", plugin_id], accepted, PLUGIN_QUERY_TIMEOUT_SECONDS)

def plugins_disable():
    plugin_id = require_plugin_id(P.get("pluginId"))
    return plugin_mutation_command(["disable", plugin_id], timeout=PLUGIN_QUERY_TIMEOUT_SECONDS)

def plugins_reload():
    plugin_id = require_plugin_id(P.get("pluginId"))
    accepted = P.get("acceptCapabilities") is True
    return plugin_mutation_command(["reload", plugin_id], accepted, PLUGIN_QUERY_TIMEOUT_SECONDS)

def plugins_update():
    plugin_id = require_plugin_id(P.get("pluginId"))
    accepted = P.get("acceptCapabilities") is True
    return plugin_mutation_command(["update", plugin_id], accepted)

def plugins_uninstall():
    plugin_id = require_plugin_id(P.get("pluginId"))
    return plugin_mutation_command(["uninstall", plugin_id, "--force"], timeout=PLUGIN_LIFECYCLE_TIMEOUT_SECONDS)

def plugins_install():
    package_spec = require_plugin_spec(P.get("packageSpec"))
    accepted = P.get("acceptCapabilities") is True
    return plugin_mutation_command(["install", package_spec, "--no-enable"], accepted)

PLUGIN_OPERATIONS = {
    "plugins_inventory": plugins_inventory,
    "plugins_search": plugins_search,
    "plugins_inspect": plugins_inspect,
    "plugins_enable": plugins_enable,
    "plugins_disable": plugins_disable,
    "plugins_reload": plugins_reload,
    "plugins_update": plugins_update,
    "plugins_uninstall": plugins_uninstall,
    "plugins_install": plugins_install,
}

def handle_plugin_action(action):
    operation = PLUGIN_OPERATIONS.get(action)
    if operation is None:
        raise ValueError("不支持的插件操作")
    print(json.dumps(operation(), ensure_ascii=False, separators=(",", ":")))
