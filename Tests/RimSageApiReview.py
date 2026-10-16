"""Read-only Streamable HTTP MCP client for public RimSage API checks.

Stores evidence inside this repository; does not register a global MCP server,
upload project sources or change game files. Returned source is review data.
"""
import argparse
import json
from pathlib import Path
from urllib.request import Request, urlopen

ENDPOINT = 'https://mcp.rimsage.com/mcp'
ROOT = Path(__file__).resolve().parents[1]


def rpc(method, params, request_id, session=None):
    body = {'jsonrpc': '2.0', 'method': method, 'params': params}
    if request_id is not None:
        body['id'] = request_id
    headers = {'Content-Type': 'application/json', 'Accept': 'application/json, text/event-stream',
               'MCP-Protocol-Version': '2024-11-05'}
    if session:
        headers['Mcp-Session-Id'] = session
    request = Request(ENDPOINT, data=json.dumps(body).encode('utf-8'), headers=headers, method='POST')
    with urlopen(request, timeout=25) as response:
        text = response.read().decode('utf-8')
        session = response.headers.get('Mcp-Session-Id', session)
    if not text.strip():
        return {}, session
    if text.lstrip().startswith('{'):
        result = json.loads(text)
    else:
        messages = [json.loads(line[5:].strip()) for line in text.splitlines() if line.startswith('data:')]
        result = next(message for message in messages if message.get('id') == request_id)
    if 'error' in result:
        raise RuntimeError(result['error'])
    return result.get('result', {}), session


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('tool', choices=('tools/list', 'search_source', 'read_file', 'list_directory', 'get_def_details', 'read_csharp_symbol'))
    parser.add_argument('arguments', nargs='?', default='{}')
    parser.add_argument('--evidence', required=True)
    args = parser.parse_args()
    target = (ROOT / args.evidence).resolve()
    if not target.is_relative_to(ROOT / '.tools'):
        raise RuntimeError('Evidence must stay within repository .tools')
    initialized, session = rpc('initialize', {'protocolVersion': '2024-11-05', 'capabilities': {},
        'clientInfo': {'name': 'ratkin-api-review', 'version': '1.0'}}, 1)
    _, session = rpc('notifications/initialized', {}, None, session)
    method = 'tools/list' if args.tool == 'tools/list' else 'tools/call'
    arguments = json.loads(args.arguments)
    params = {} if args.tool == 'tools/list' else {'name': args.tool, 'arguments': arguments}
    result, _ = rpc(method, params, 2, session)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps({'endpoint': ENDPOINT, 'server': initialized.get('serverInfo'),
        'tool': args.tool, 'arguments': arguments, 'result': result}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
