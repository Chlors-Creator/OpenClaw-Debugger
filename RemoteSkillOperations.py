SKILL_QUERY_TIMEOUT_SECONDS = 90
SKILL_UPDATE_TIMEOUT_SECONDS = 900

def require_skill_name(value):
    if not isinstance(value, str) or not re.fullmatch(r"[A-Za-z0-9@][A-Za-z0-9@._/-]*", value):
        raise ValueError("技能名称格式无效")
    if any(part in (".", "..") for part in value.split("/")):
        raise ValueError("技能名称包含无效路径片段")
    return value

def skill_command(arguments, timeout=SKILL_QUERY_TIMEOUT_SECONDS, expect_json=True):
    try:
        completed = subprocess.run(
            ["openclaw", "skills", *arguments],
            capture_output=True, text=True, timeout=timeout, env=os.environ.copy())
    except FileNotFoundError as error:
        raise RuntimeError("服务器找不到 openclaw 命令，请确认 OpenClaw 已安装") from error
    except subprocess.TimeoutExpired as error:
        raise TimeoutError("OpenClaw 技能命令超时") from error
    stdout = (completed.stdout or "").strip()
    stderr = redact_cli_error(completed.stderr or "").strip()
    if completed.returncode != 0:
        detail = stderr or redact_cli_error(stdout)
        raise RuntimeError("OpenClaw 技能命令失败" + (": " + detail if detail else ""))
    response = {"ok": True}
    if expect_json:
        try:
            response["result"] = json.loads(stdout)
        except json.JSONDecodeError as error:
            raise ValueError("OpenClaw 技能命令没有返回有效 JSON：" + redact_cli_error(stdout)) from error
    else:
        response["message"] = stdout
    if stderr:
        response["diagnostics"] = stderr
    return response

def skills_inventory():
    return skill_command(["list", "--json"])

def skills_info():
    name = require_skill_name(P.get("skillName"))
    return skill_command(["info", name, "--json"])

def skills_update():
    name = require_skill_name(P.get("skillName"))
    return skill_command(["update", name], SKILL_UPDATE_TIMEOUT_SECONDS, False)

SKILL_OPERATIONS = {
    "skills_inventory": skills_inventory,
    "skills_info": skills_info,
    "skills_update": skills_update,
}

def handle_skill_action(action):
    operation = SKILL_OPERATIONS.get(action)
    if operation is None:
        raise ValueError("不支持的技能操作")
    print(json.dumps(operation(), ensure_ascii=False, separators=(",", ":")))
