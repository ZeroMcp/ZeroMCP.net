namespace ZeroMCP.Ui;

/// <summary>
/// Embedded HTML for the MCP Tool Inspector UI (GET {RoutePrefix}/ui).
/// Swagger-like test invocation interface for MCP tools.
/// </summary>
internal static class McpInspectorUiHtml
{
    private const string PlaceholderBase = "{{MCP_BASE}}";
    private const string PlaceholderRoot = "{{MCP_ROOT}}";
    private const string PlaceholderVersions = "{{AVAILABLE_VERSIONS_JSON}}";
    private const string PlaceholderCurrentVersion = "{{CURRENT_VERSION_JSON}}";
    private const string PlaceholderShowSelector = "{{SHOW_VERSION_SELECTOR}}";

    /// <summary>
    /// Returns the full HTML document with the MCP base path injected for fetch calls.
    /// When <paramref name="availableVersions"/> has more than one version, a version selector is shown.
    /// </summary>
    /// <param name="mcpBasePath">The route prefix for the current page (e.g. "/mcp" or "/mcp/v1") with no trailing slash.</param>
    /// <param name="currentVersion">The version number for this endpoint, or null for "latest" (unversioned /mcp).</param>
    /// <param name="availableVersions">All registered version numbers; when count &gt; 1, the version selector is shown.</param>
    public static string GetHtml(string mcpBasePath, int? currentVersion = null, IReadOnlyList<int>? availableVersions = null)
    {
        var showSelector = availableVersions is { Count: > 1 };
        var rootBase = GetRootBase(mcpBasePath, currentVersion, availableVersions);

        var escapedBase = mcpBasePath.Replace("\\", "\\\\").Replace("'", "\\'");
        var escapedRoot = rootBase.Replace("\\", "\\\\").Replace("'", "\\'");
        var versionsJson = showSelector && availableVersions is { } v ? "[" + string.Join(",", v) + "]" : "[]";
        var currentVersionJson = currentVersion.HasValue ? currentVersion.Value.ToString() : "\"latest\"";
        var showSelectorStr = showSelector ? "true" : "false";

        return HtmlTemplate
            .Replace(PlaceholderBase, escapedBase)
            .Replace(PlaceholderRoot, escapedRoot)
            .Replace(PlaceholderVersions, versionsJson)
            .Replace(PlaceholderCurrentVersion, currentVersionJson)
            .Replace(PlaceholderShowSelector, showSelectorStr);
    }

    private static string GetRootBase(string mcpBasePath, int? currentVersion, IReadOnlyList<int>? availableVersions)
    {
        if (availableVersions is not { Count: > 0 } || !currentVersion.HasValue)
            return mcpBasePath;
        var suffix = "/v" + currentVersion.Value;
        if (mcpBasePath.EndsWith(suffix, StringComparison.Ordinal))
            return mcpBasePath[..^suffix.Length];
        return mcpBasePath;
    }

    private const string HtmlTemplate = """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8"/>
    <meta name="viewport" content="width=device-width, initial-scale=1"/>
    <title>ZeroMCP Tool Inspector</title>
    <style>
        * { box-sizing: border-box; }
        body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif; margin: 0; background: #fafafa; color: #3b4151; line-height: 1.6; }
        .topbar { background: linear-gradient(to bottom, #89bf04, #6b9a00); padding: 12px 20px; color: #fff; }
        .topbar h1 { margin: 0; font-size: 1.5rem; font-weight: 600; }
        .topbar a { color: #fff; opacity: 0.9; text-decoration: none; margin-left: 12px; font-size: 0.9rem; }
        .topbar a:hover { opacity: 1; text-decoration: underline; }
        .container { max-width: 960px; margin: 0 auto; padding: 24px; }
        .tool { background: #fff; border: 1px solid #e0e0e0; border-radius: 4px; margin-bottom: 16px; overflow: hidden; }
        .tool-header { padding: 14px 20px; cursor: pointer; display: flex; align-items: center; justify-content: space-between; }
        .tool-header:hover { background: #f7f7f7; }
        .tool-method { font-weight: 600; color: #6b9a00; font-size: 0.85rem; margin-right: 8px; min-width: 3rem; }
        .tool-name { font-weight: 600; font-size: 1rem; }
        .tool-desc { color: #6b7280; font-size: 0.9rem; margin-top: 4px; }
        .tool-body { padding: 0 20px 20px; border-top: 1px solid #e8e8e8; }
        .tool-section { margin-top: 16px; }
        .tool-section h4 { margin: 0 0 8px; font-size: 0.85rem; color: #6b7280; text-transform: uppercase; letter-spacing: 0.05em; }
        pre { background: #1e1e1e; color: #d4d4d4; padding: 12px; border-radius: 4px; overflow-x: auto; font-size: 0.8rem; margin: 0; }
        textarea { width: 100%; min-height: 80px; font-family: ui-monospace, monospace; font-size: 0.85rem; padding: 10px; border: 1px solid #e0e0e0; border-radius: 4px; }
        button { background: #6b9a00; color: #fff; border: none; padding: 8px 16px; border-radius: 4px; cursor: pointer; font-size: 0.9rem; }
        button:hover { background: #5a8500; }
        button:disabled { background: #9ca3af; cursor: not-allowed; }
        .response { margin-top: 12px; }
        .response-error { border-left: 4px solid #dc2626; }
        .loading { color: #6b7280; padding: 24px; text-align: center; }
        .error { color: #dc2626; padding: 12px; background: #fef2f2; border-radius: 4px; }
        .category-group { margin-bottom: 28px; }
        .category-group h2 { margin: 0 0 12px; font-size: 1.1rem; font-weight: 600; color: #374151; text-transform: capitalize; letter-spacing: 0.02em; padding-bottom: 6px; border-bottom: 1px solid #e5e7eb; }
        .version-badge { font-size: 0.7rem; font-weight: 500; color: #6b9a00; background: rgba(255,255,255,0.3); padding: 2px 6px; border-radius: 3px; margin-left: 6px; }
        .streaming-badge { font-size: 0.65rem; font-weight: 600; color: #fff; background: #7c3aed; padding: 2px 6px; border-radius: 3px; margin-left: 6px; letter-spacing: 0.04em; }
        .topbar select { margin-left: 12px; padding: 4px 8px; border-radius: 4px; border: 1px solid rgba(255,255,255,0.5); background: rgba(255,255,255,0.2); color: #fff; font-size: 0.9rem; }
    </style>
</head>
<body>
    <div class="topbar">
        <h1>ZeroMCP Tool Inspector</h1>
        <span id="version-selector-wrap" style="display:none;">Version: <select id="version-select"></select></span>
        <a href="#" id="link-json">JSON (tools)</a>
    </div>
    <div class="container">
        <div id="loading" class="loading">Loading tools…</div>
        <div id="tools" style="display:none;"></div>
    </div>
    <script>
        const MCP_BASE = '{{MCP_BASE}}';
        const MCP_ROOT = '{{MCP_ROOT}}';
        const AVAILABLE_VERSIONS = {{AVAILABLE_VERSIONS_JSON}};
        const CURRENT_VERSION = {{CURRENT_VERSION_JSON}};
        const SHOW_VERSION_SELECTOR = {{SHOW_VERSION_SELECTOR}};

        let currentInvokeBase = MCP_BASE;
        document.getElementById('link-json').href = MCP_BASE + '/tools';

        if (SHOW_VERSION_SELECTOR && AVAILABLE_VERSIONS.length > 0) {
            const wrap = document.getElementById('version-selector-wrap');
            const sel = document.getElementById('version-select');
            wrap.style.display = 'inline';
            sel.innerHTML = '<option value="latest">latest</option>' + AVAILABLE_VERSIONS.map(v => '<option value="' + v + '">v' + v + '</option>').join('');
            sel.value = typeof CURRENT_VERSION === 'number' ? CURRENT_VERSION : 'latest';
            sel.addEventListener('change', () => {
                const v = sel.value;
                currentInvokeBase = v === 'latest' ? MCP_ROOT : MCP_ROOT + '/v' + v;
                document.getElementById('link-json').href = currentInvokeBase + '/tools';
                document.getElementById('loading').style.display = 'block';
                document.getElementById('loading').innerHTML = 'Loading tools…';
                document.getElementById('tools').style.display = 'none';
                document.getElementById('tools').innerHTML = '';
                loadTools().then(renderTools).catch(e => {
                    document.getElementById('loading').innerHTML = '<div class="error">' + escapeHtml(e.message || String(e)) + '</div>';
                });
            });
        }

        async function loadTools() {
            const toolsUrl = currentInvokeBase + '/tools';
            const res = await fetch(toolsUrl);
            if (!res.ok) throw new Error(res.status + ' ' + res.statusText);
            return res.json();
        }

        function renderTools(data) {
            const container = document.getElementById('tools');
            const loading = document.getElementById('loading');
            loading.style.display = 'none';
            container.style.display = 'block';
            container.innerHTML = '';
            const UNCAT = '(Uncategorized)';
            const groups = {};
            for (const tool of data.tools || []) {
                const cat = (tool.category && String(tool.category).trim()) ? String(tool.category).trim() : UNCAT;
                if (!groups[cat]) groups[cat] = [];
                groups[cat].push(tool);
            }
            const sortedCats = Object.keys(groups).sort((a, b) => (a === UNCAT ? 1 : b === UNCAT ? -1 : a.localeCompare(b)));
            for (const category of sortedCats) {
                const groupDiv = document.createElement('div');
                groupDiv.className = 'category-group';
                groupDiv.innerHTML = '<h2>' + escapeHtml(category) + '</h2>';
                const listDiv = document.createElement('div');
                groupDiv.appendChild(listDiv);
                for (const tool of groups[category]) {
                    const versionBadge = (tool.version != null) ? '<span class="version-badge">v' + tool.version + '</span>' : '';
                    const streamBadge = tool.isStreaming ? '<span class="streaming-badge">STREAMING</span>' : '';
                    const el = document.createElement('div');
                    el.className = 'tool';
                    el.dataset.streaming = tool.isStreaming ? '1' : '';
                    el.innerHTML = `
                        <div class="tool-header">
                            <span><span class="tool-method">${escapeHtml(tool.httpMethod || '')}</span><span class="tool-name">${escapeHtml(tool.name)}</span>${versionBadge}${streamBadge}</span>
                        </div>
                        <div class="tool-body" style="display:none;">
                            <div class="tool-desc">${escapeHtml(tool.description || '')}</div>
                            <div class="tool-section">
                                <h4>Input schema</h4>
                                <pre>${escapeHtml(JSON.stringify(tool.inputSchema || {}, null, 2))}</pre>
                            </div>
                            <div class="tool-section">
                                <h4>Try it out</h4>
                                <textarea placeholder='{"key": "value"}' class="args-input">${defaultArgs(tool.inputSchema)}</textarea>
                                <br/><br/>
                                <button type="button" class="invoke-btn">Invoke</button>
                                <div class="response" style="display:none;"></div>
                            </div>
                        </div>
                    `;
                    listDiv.appendChild(el);
                    const header = el.querySelector('.tool-header');
                    const body = el.querySelector('.tool-body');
                    header.addEventListener('click', () => { body.style.display = body.style.display ? '' : 'block'; });
                    el.querySelector('.invoke-btn').addEventListener('click', () => invoke(tool.name, el));
                }
                container.appendChild(groupDiv);
            }
        }

        function defaultArgs(schema) {
            if (!schema || !schema.properties) return '{}';
            const obj = {};
            for (const [k, v] of Object.entries(schema.properties)) {
                if (v.type === 'integer' || v.type === 'number') obj[k] = 0;
                else if (v.type === 'boolean') obj[k] = false;
                else if (v.type === 'array') obj[k] = [];
                else obj[k] = '';
            }
            return JSON.stringify(obj, null, 2);
        }

        function escapeHtml(s) {
            const div = document.createElement('div');
            div.textContent = s;
            return div.innerHTML;
        }
        async function invoke(toolName, toolEl) {
            const textarea = toolEl.querySelector('.args-input');
            const responseEl = toolEl.querySelector('.response');
            const isStreaming = toolEl.dataset.streaming === '1';
            let args = {};
            try {
                args = JSON.parse(textarea.value || '{}');
            } catch (e) {
                responseEl.style.display = 'block';
                responseEl.className = 'response response-error';
                responseEl.innerHTML = '<pre>' + escapeHtml('Invalid JSON: ' + e.message) + '</pre>';
                return;
            }
            const btn = toolEl.querySelector('.invoke-btn');
            btn.disabled = true;
            responseEl.style.display = 'block';
            responseEl.className = 'response';
            responseEl.innerHTML = '<div class="loading">Calling…</div>';
            try {
                if (isStreaming) {
                    const res = await fetch(currentInvokeBase, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'tools/call', params: { name: toolName, arguments: args } })
                    });
                    const reader = res.body.getReader();
                    const decoder = new TextDecoder();
                    let buffer = '';
                    let chunks = [];
                    responseEl.innerHTML = '<pre></pre>';
                    const pre = responseEl.querySelector('pre');
                    while (true) {
                        const { done, value } = await reader.read();
                        if (done) break;
                        buffer += decoder.decode(value, { stream: true });
                        const lines = buffer.split('\\n');
                        buffer = lines.pop();
                        for (const line of lines) {
                            if (line.startsWith('data: ')) {
                                try {
                                    const parsed = JSON.parse(line.substring(6));
                                    const meta = parsed.result?._meta;
                                    if (meta?.status === 'streaming') {
                                        const txt = parsed.result?.content?.[0]?.text || '';
                                        chunks.push(txt);
                                        pre.textContent = chunks.map((c,i) => '[' + i + '] ' + c).join('\\n');
                                    } else if (meta?.status === 'done') {
                                        pre.textContent += '\\n--- Done (' + (meta.totalChunks || 0) + ' chunks) ---';
                                    } else if (meta?.status === 'error') {
                                        responseEl.classList.add('response-error');
                                        pre.textContent += '\\nERROR: ' + (parsed.result?.content?.[0]?.text || 'unknown');
                                    }
                                } catch (_) {}
                            }
                        }
                    }
                } else {
                    const res = await fetch(currentInvokeBase, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'tools/call', params: { name: toolName, arguments: args } })
                    });
                    const data = await res.json();
                    const hasError = data.error || (data.result && data.result.isError);
                    if (hasError) responseEl.classList.add('response-error');
                    responseEl.innerHTML = '<pre>' + escapeHtml(JSON.stringify(data, null, 2)) + '</pre>';
                }
            } catch (e) {
                responseEl.classList.add('response-error');
                responseEl.innerHTML = '<pre>' + escapeHtml(e.message || String(e)) + '</pre>';
            }
            btn.disabled = false;
        }

        loadTools().then(renderTools).catch(e => {
            document.getElementById('loading').innerHTML = '<div class="error">' + escapeHtml(e.message || String(e)) + '</div>';
        });
    </script>
</body>
</html>
""";
}
