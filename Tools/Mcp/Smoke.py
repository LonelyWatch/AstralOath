"""Real local MCP initialize/catalog smoke; --editor also queries the selected Unity instance."""
import argparse
import asyncio
import json
import os
from pathlib import Path

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


async def run(editor: bool):
    repo = Path(__file__).resolve().parents[2]
    env = dict(os.environ, UNITY_MCP_DISABLE_TELEMETRY="true", DISABLE_TELEMETRY="true", PYTHONUTF8="1")
    parameters = StdioServerParameters(
        command=str(repo / ".tools/mcp-venv/Scripts/python.exe"),
        args=["-m", "main", "--transport", "stdio"],
        cwd=str(repo / "Tools/Mcp/Server"),
        env=env,
    )
    async with asyncio.timeout(60):
        async with stdio_client(parameters) as (reader, writer):
            async with ClientSession(reader, writer) as session:
                initialized = await session.initialize()
                tools = await session.list_tools()
                names = sorted(tool.name for tool in tools.tools)
                if "set_active_instance" not in names:
                    raise RuntimeError("Unity MCP instance-selection tool missing")
                print(json.dumps({"server": initialized.serverInfo.name, "version": initialized.serverInfo.version, "tool_count": len(names), "tools": names}, ensure_ascii=False))
                if editor:
                    instances = await session.read_resource("mcpforunity://instances")
                    print(instances.model_dump_json())
                    inventory = json.loads(instances.contents[0].text)
                    expected = (repo / ".artifacts/UnityProject/Assets").resolve()
                    matches = [item for item in inventory.get("instances", []) if Path(item["path"]).resolve() == expected]
                    if len(matches) != 1:
                        raise RuntimeError("Expected exactly one isolated foundation Unity editor")
                    selected = await session.call_tool("set_active_instance", {"instance": matches[0]["id"]})
                    if selected.isError or not json.loads(selected.content[0].text).get("success"):
                        raise RuntimeError("Unity instance selection failed")
                    state = await session.read_resource("mcpforunity://editor/state")
                    print(state.model_dump_json())
                    observed = json.loads(state.contents[0].text)
                    if not observed.get("success") or observed["data"]["unity"]["instance_id"] != matches[0]["id"]:
                        raise RuntimeError("Unity editor state did not match selected instance")
                # EOF lets the server drain notifications while ClientSession still consumes them.
                await writer.aclose()
                await asyncio.sleep(0.2)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--editor", action="store_true")
    asyncio.run(run(parser.parse_args().editor))
