        let currentGroupName = '';

        // ================================================================
        // 主页：显示群组卡片
        // ================================================================
        async function showHome() {
            const content = document.getElementById('content');
            content.innerHTML = '<div class="loading">加载中...</div>';

            try {
                const response = await fetch(`/api/groups?t=${Date.now()}`);
                const groups = await response.json();

                if (groups.length === 0) {
                    content.innerHTML = '<div class="empty-state">暂无群组数据。请先运行 QQPilot4.exe 抓取 QQ 聊天记录。</div>';
                    return;
                }

                let html = '';
                groups.forEach(group => {
                    html += `
                        <div class="card">
                            <div class="card-header">
                                <span class="card-icon">📢</span>
                                <h3 class="card-title">${escapeHtml(group)}</h3>
                            </div>
                            <div class="card-actions card-actions-multi">
                                <button class="btn-primary" onclick="showConfirmDialog('${escapeHtml(group)}')">📝 全量总结</button>
                                <button class="btn-blue" onclick="showMapReduceDialog('${escapeHtml(group)}')">🧩 Map-Reduce 总结</button>
                                <button class="btn-secondary" onclick="showRagPanel('${escapeHtml(group)}')">🔍 话题检索/RAG</button>
                                <button class="btn-secondary" onclick="showComments('${escapeHtml(group)}')">查看聊天</button>
                            </div>
                        </div>
                    `;
                });
                content.innerHTML = html;
            } catch (error) {
                content.innerHTML = `<div class="error-message">加载失败: ${error.message}</div>`;
            }
        }

        // ================================================================
        // 原有：普通全量总结
        // ================================================================
        function showConfirmDialog(groupName) {
            currentGroupName = groupName;
            document.getElementById('confirmMessage').textContent = `确定要对「${currentGroupName}」进行全量一次性总结吗？\n（适用于聊天记录较短的情况，过长会超出模型上下文）`;
            document.getElementById('confirmModal').style.display = 'flex';
        }

        function closeModal() {
            document.getElementById('confirmModal').style.display = 'none';
            document.getElementById('mapReduceModal').style.display = 'none';
        }

        function confirmSummary() {
            closeModal();
            confirmSummaryNext(currentGroupName);
        }

        async function confirmSummaryNext(gname) {
            const content = document.getElementById('content');
            content.innerHTML = '<div class="loading">正在总结中...<br><small>提示：聊天记录很多时建议使用 Map-Reduce 总结</small></div>';

            try {
                const response = await fetch('/api/summary', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ groupName: gname })
                });

                const result = await response.json();
                if (result.success) {
                    showSummaryResult(gname, result.summary, result.fileName);
                } else {
                    content.innerHTML = `<div class="error-message">总结失败: ${result.message}</div>`;
                }
            } catch (error) {
                content.innerHTML = `<div class="error-message">总结失败: ${error.message}</div>`;
            }
        }

        // ================================================================
        // Map-Reduce 总结
        // ================================================================
        function showMapReduceDialog(groupName) {
            currentGroupName = groupName;
            document.getElementById('mrGroupName').textContent = groupName;
            // 默认分块大小从后端配置拿
            fetch('/api/config').then(r => r.json()).then(cfg => {
                document.getElementById('mrChunkSize').value = cfg.mapReduceChunkSize || 60;
            }).catch(() => { document.getElementById('mrChunkSize').value = 60; });
            document.getElementById('mapReduceModal').style.display = 'flex';
        }

        function confirmMapReduce() {
            closeModal();
            const chunkSize = parseInt(document.getElementById('mrChunkSize').value) || 60;
            runMapReduce(currentGroupName, chunkSize);
        }

        async function runMapReduce(gname, messagesPerChunk) {
            const content = document.getElementById('content');
            content.innerHTML = `
                <div class="loading">
                    🧩 正在执行 Map-Reduce 总结...<br>
                    <small>
                        阶段 1/2：把聊天记录按每块 ${messagesPerChunk} 条消息切块，分别总结中...<br>
                        阶段 2/2：最后整合小块总结，生成最终归纳
                    </small>
                </div>
            `;

            try {
                const response = await fetch('/api/summary/mapreduce', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ groupName: gname, messagesPerChunk })
                });
                const result = await response.json();

                if (result.success) {
                    showSummaryResult(gname, result.summary, result.fileName, 'Map-Reduce');
                } else {
                    content.innerHTML = `<div class="error-message">Map-Reduce 总结失败: ${result.message}</div>`;
                }
            } catch (error) {
                content.innerHTML = `<div class="error-message">Map-Reduce 总结失败: ${error.message}</div>`;
            }
        }

        // ================================================================
        // 显示总结结果（统一界面）
        // ================================================================
        function showSummaryResult(groupName, summary, fileName, mode = '普通') {
            const content = document.getElementById('content');
            content.innerHTML = `
                <div class="summary-container">
                    <h2>${escapeHtml(groupName)} - 总结结果 <small style="font-size:14px;color:#888;">（${mode}模式）</small></h2>
                    ${fileName ? `<div style="color:#888;font-size:13px;margin-bottom:10px;">已保存到：summaries/${escapeHtml(fileName)}</div>` : ''}
                    <textarea id="summary" class="summary-text" readonly>${escapeHtml(summary)}</textarea>
                    <div style="margin-top:15px;display:flex;gap:10px;flex-wrap:wrap;">
                        <button class="btn-secondary" onclick="copySummary()">📋 复制</button>
                        <button class="btn-secondary" onclick="showHome()">← 返回主页</button>
                    </div>
                </div>
            `;
        }

        function copySummary() {
            const ta = document.getElementById('summary');
            if (!ta) return;
            ta.select();
            document.execCommand('copy');
            alert('已复制到剪贴板');
        }

        // ================================================================
        // 查看聊天记录
        // ================================================================
        async function showComments(groupName) {
            const content = document.getElementById('content');
            content.innerHTML = '<div class="loading">加载聊天记录中...</div>';

            try {
                const response = await fetch(`/api/comments/${encodeURIComponent(groupName)}?t=${Date.now()}`);
                const comments = await response.json();

                if (comments.length === 0) {
                    content.innerHTML = `
                        <div class="empty-state">
                            <p>「${escapeHtml(groupName)}」暂无聊天记录</p>
                            <button class="btn-secondary" onclick="showHome()">返回主页</button>
                        </div>
                    `;
                    return;
                }

                let html = `
                    <div class="comments-container">
                        <h2>${escapeHtml(groupName)} - 聊天记录 <small style="font-size:13px;color:#888;">共 ${comments.length} 条</small></h2>
                        <div class="comments-list">
                `;

                comments.forEach(comment => {
                    let imageHtml = '';
                    if (comment.images && comment.images.length > 0) {
                        imageHtml = '<div class="comment-images">';
                        comment.images.forEach(imgBase64 => {
                            imageHtml += `<img src="${imgBase64}" alt="图片" class="comment-image" width="200" >`;
                        });
                        imageHtml += '</div>';
                    }

                    html += `
                        <div class="comment-item">
                            <span class="comment-user">${escapeHtml(comment.username || '未知用户')}</span>
                            <span class="comment-time">${escapeHtml(comment.time || '')}</span>
                            <p class="comment-text">${escapeHtml(comment.text || '')}</p>
                            ${imageHtml}
                        </div>
                    `;
                });

                html += `
                        </div>
                        <button class="btn-secondary" style="margin-top: 15px;" onclick="showHome()">返回主页</button>
                    </div>
                `;

                content.innerHTML = html;
            } catch (error) {
                content.innerHTML = `<div class="error-message">加载聊天记录失败: ${error.message}</div>`;
            }
        }

        // ================================================================
        // RAG 面板：建立索引 + 话题查询
        // ================================================================
        async function showRagPanel(groupName) {
            currentGroupName = groupName;
            const content = document.getElementById('content');
            content.innerHTML = `<div class="loading">正在加载 RAG 面板...</div>`;

            try {
                const statusResp = await fetch(`/api/rag/status/${encodeURIComponent(groupName)}`);
                const status = await statusResp.json();

                let html = `
                    <div class="rag-panel">
                        <h2>🔍 话题检索 / RAG 总结 - ${escapeHtml(groupName)}</h2>
                        <button class="btn-secondary" style="margin-bottom:15px;" onclick="showHome()">← 返回主页</button>

                        <div class="card rag-status-card">
                            <div class="card-header">
                                <span class="card-icon">${status.indexed ? '✅' : '⚠️'}</span>
                                <h3 class="card-title">索引状态：${status.indexed ? '已建立索引' : '尚未建立索引'}</h3>
                            </div>
                            <div class="card-actions" style="margin-top:10px;">
                                <button class="btn-blue" onclick="ragIndexGroup('${escapeHtml(groupName)}')">
                                    ${status.indexed ? '🔄 重建索引' : '📇 建立索引'}
                                </button>
                                <small style="color:#888;margin-left:10px;align-self:center;">
                                    首次使用请先建立索引；聊天记录更新后需重建
                                </small>
                            </div>
                            <div id="ragIndexStatus" style="margin-top:10px;"></div>
                        </div>

                        <div class="card rag-query-card">
                            <div class="card-header">
                                <span class="card-icon">💬</span>
                                <h3 class="card-title">指定话题总结</h3>
                            </div>
                            <p style="color:#666;font-size:13px;margin:5px 0 15px 0;">
                                例如：「上周关于项目进度的讨论」、「@张三 提到的需求变更」、「团建是怎么安排的」……
                                <br>会先用语义检索找出相关聊天片段，再交给 AI 针对性总结。
                            </p>
                            <div style="display:flex;gap:8px;flex-wrap:wrap;">
                                <input type="text" id="ragQueryInput" class="rag-query-input"
                                    placeholder="请输入你想总结的话题，例如：总结一下上周关于项目进度的讨论"
                                    onkeydown="if(event.key==='Enter')ragRunQuery()">
                                <button class="btn-primary" onclick="ragRunQuery()">🎯 检索并总结</button>
                            </div>
                            <div style="margin-top:10px;font-size:12px;color:#888;">
                                <label>返回 Top <input type="number" id="ragTopK" value="8" min="1" max="30" style="width:55px;"> 个相关片段，
                                最低相似度：<input type="number" id="ragMinSim" value="0.25" step="0.05" min="0" max="1" style="width:60px;"></label>
                            </div>
                            <div id="ragQueryStatus" style="margin-top:10px;"></div>
                        </div>

                        <div id="ragResult"></div>
                    </div>
                `;
                content.innerHTML = html;
            } catch (e) {
                content.innerHTML = `<div class="error-message">加载 RAG 面板失败: ${e.message}</div>`;
            }
        }

        // 建立 RAG 索引
        async function ragIndexGroup(groupName) {
            const statusEl = document.getElementById('ragIndexStatus');
            statusEl.innerHTML = '<div class="loading" style="padding:10px;">正在建立向量索引，分块生成 embedding……（聊天记录多的话会比较慢，请耐心等待）</div>';

            try {
                const resp = await fetch('/api/rag/index', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ groupName })
                });
                const r = await resp.json();
                if (r.success) {
                    statusEl.innerHTML = `<div class="success-message">✅ ${r.message}（共 ${r.chunkCount} 个块）</div>`;
                    setTimeout(() => showRagPanel(groupName), 1500);
                } else {
                    statusEl.innerHTML = `<div class="error-message">❌ ${r.message}</div>`;
                }
            } catch (e) {
                statusEl.innerHTML = `<div class="error-message">❌ 请求失败: ${e.message}</div>`;
            }
        }

        // 执行 RAG 查询
        async function ragRunQuery() {
            const query = (document.getElementById('ragQueryInput').value || '').trim();
            const topK = parseInt(document.getElementById('ragTopK').value) || 8;
            const minSim = parseFloat(document.getElementById('ragMinSim').value) || 0.25;
            const statusEl = document.getElementById('ragQueryStatus');
            const resultEl = document.getElementById('ragResult');

            if (!query) {
                statusEl.innerHTML = '<div class="error-message">请先输入查询内容</div>';
                return;
            }

            statusEl.innerHTML = '<div class="loading" style="padding:10px;">🔍 正在生成查询向量并语义检索...</div>';
            resultEl.innerHTML = '';

            try {
                const resp = await fetch('/api/rag/query', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ groupName: currentGroupName, query, topK, minSimilarity: minSim })
                });
                const r = await resp.json();
                statusEl.innerHTML = '';

                if (!r.success) {
                    resultEl.innerHTML = `<div class="error-message">❌ ${r.message}</div>`;
                    return;
                }

                // 展示结果
                let html = `
                    <div class="rag-result-card">
                        <h3>📋 总结结果</h3>
                        <div style="color:#888;font-size:13px;margin-bottom:10px;">
                            查询：${escapeHtml(r.query)} ｜ 命中 ${r.retrievedChunks ? r.retrievedChunks.length : 0} 个相关片段
                        </div>
                        <textarea class="summary-text" readonly style="min-height:220px;">${escapeHtml(r.summary || '')}</textarea>
                    </div>
                `;

                if (r.retrievedChunks && r.retrievedChunks.length > 0) {
                    html += `<div class="rag-retrieved-card"><h3>🔎 检索到的相关片段（按相似度排序）</h3>`;
                    r.retrievedChunks.forEach((c, idx) => {
                        html += `
                            <div class="rag-chunk">
                                <div class="rag-chunk-head">
                                    <b>#${idx + 1}</b> ｜ 相似度: <b>${c.similarity.toFixed(3)}</b>
                                    ｜ 时间: ${escapeHtml(c.timeRange || '—')}
                                    ｜ ${escapeHtml(c.messageCount + '条消息')}
                                </div>
                                <div class="rag-chunk-head">参与: ${escapeHtml(c.usernames || '—')}</div>
                                <pre class="rag-chunk-text">${escapeHtml(c.chunkText || '')}</pre>
                            </div>
                        `;
                    });
                    html += `</div>`;
                }

                resultEl.innerHTML = html;
            } catch (e) {
                statusEl.innerHTML = '';
                resultEl.innerHTML = `<div class="error-message">❌ 请求失败: ${e.message}</div>`;
            }
        }

        // ================================================================
        // 设置页面
        // ================================================================
        async function showSettings() {
            const content = document.getElementById('content');
            content.innerHTML = '<div class="loading">加载设置中...</div>';

            try {
                const response = await fetch('/api/config');
                const config = await response.json();

                const settingsHtml = `
                    <div class="settings-form" id="settingsForm">
                        <h2>系统设置</h2>

                        <!-- ============== 原有 LLM 设置 ============== -->
                        <fieldset class="settings-group">
                            <legend>🤖 大模型 (LLM) 设置</legend>

                            <div class="form-group">
                                <label class="setting-key">版本</label>
                                <input type="text" id="version" value="${escapeHtml(config.version || '')}" readonly>
                            </div>
                            <div class="form-group">
                                <label class="setting-key">模型名称</label>
                                <input type="text" id="modelname" value="${escapeHtml(config.modelName || '')}">
                            </div>
                            <div class="form-group">
                                <label class="setting-key">视觉模型</label>
                                <label class="setting-key"><input type="radio" name="isvisionmodel" value="true" ${config.isVisionModel === true ? 'checked' : ''}> 是</label>
                                <label class="setting-key"><input type="radio" name="isvisionmodel" value="false" ${config.isVisionModel !== true ? 'checked' : ''}> 否</label>
                            </div>
                            <div class="form-group">
                                <label class="setting-key">API Key</label>
                                <input type="text" id="api_key" value="${escapeHtml(config.apiKey || '')}">
                            </div>
                            <div class="form-group">
                                <label class="setting-key">服务器地址</label>
                                <input type="text" id="server_url" value="${escapeHtml(config.serverUrl || '')}">
                                <small style="color:#888;">填 ollama 会自动用 http://localhost:11434/v1 ；填 custom 就写完整 URL 如 https://xxx/v1</small>
                            </div>
                            <div class="form-group">
                                <label class="setting-key">超时时间 (秒)</label>
                                <input type="number" id="remote_server_timeout" value="${config.remoteServerTimeout != null ? config.remoteServerTimeout : ''}">
                            </div>
                            <div class="form-group">
                                <label class="setting-key">最大图片数量</label>
                                <input type="number" min="1" id="maximagecount" value="${config.maxImageCount != null ? config.maxImageCount : '1'}">
                            </div>
                            <div class="form-group">
                                <label class="setting-key">System Prompt</label>
                                <textarea id="SystemContent">${escapeHtml(config.systemContent || '')}</textarea>
                            </div>
                        </fieldset>

                        <!-- ============== 新增：Embedding 配置 ============== -->
                        <fieldset class="settings-group">
                            <legend>🧠 词嵌入 (Embedding) 设置 <small style="font-weight:normal;color:#888;">—— 用于 RAG 语义检索</small></legend>

                            <div class="form-group">
                                <label class="setting-key">Embedding 服务器地址</label>
                                <input type="text" id="embedding_server_url" value="${escapeHtml(config.embeddingServerUrl || '')}">
                                <small style="color:#888;">
                                    填 <code>ollama</code> 会自动转 http://localhost:11434/v1 ；
                                    测试时 Ollama 拉 <code>qwen3-embedding:0.6b</code>
                                </small>
                            </div>
                            <div class="form-group">
                                <label class="setting-key">Embedding API Key</label>
                                <input type="text" id="embedding_api_key" value="${escapeHtml(config.embeddingApiKey || '')}">
                                <small style="color:#888;">Ollama 本地不需要填，留空即可</small>
                            </div>
                            <div class="form-group">
                                <label class="setting-key">Embedding 模型名称</label>
                                <input type="text" id="embedding_model_name" value="${escapeHtml(config.embeddingModelName || '')}">
                                <small style="color:#888;">默认 qwen3-embedding:0.6b</small>
                            </div>
                        </fieldset>

                        <!-- ============== 新增：Map-Reduce 默认参数 ============== -->
                        <fieldset class="settings-group">
                            <legend>🧩 Map-Reduce 摘要参数</legend>
                            <div class="form-group">
                                <label class="setting-key">每块消息数 (权重)</label>
                                <input type="number" id="mapreduce_chunksize" min="5" max="500"
                                    value="${config.mapReduceChunkSize != null ? config.mapReduceChunkSize : 60}">
                                <small style="color:#888;">数值越大单块总结越详细，块数越少；60 条/块是比较均衡的默认值</small>
                            </div>
                            <div class="form-group">
                                <label class="setting-key">按字符数分块</label>
                                <label class="setting-key">
                                    <input type="radio" name="mapreduce_charbased" value="true" ${config.mapReduceUseCharBased === true ? 'checked' : ''}> 是
                                </label>
                                <label class="setting-key">
                                    <input type="radio" name="mapreduce_charbased" value="false" ${config.mapReduceUseCharBased !== true ? 'checked' : ''}> 否（按消息数，推荐）
                                </label>
                            </div>
                        </fieldset>

                        <!-- ============== 抓取/UI 设置（隐藏不常用的）============== -->
                        <details style="margin-top:15px;">
                            <summary style="cursor:pointer;color:#666;">⚙️ 其他 / 抓取相关设置</summary>

                            <div class="form-group">
                                <label class="setting-key">宽度</label>
                                <input type="number" id="width" value="${config.width != null ? config.width : ''}">
                            </div>
                            <div class="form-group">
                                <label class="setting-key">高度</label>
                                <input type="number" id="height" value="${config.height != null ? config.height : ''}">
                            </div>
                            <div class="form-group">
                                <label class="setting-key">滚动次数</label>
                                <input type="number" id="scroll" value="${config.scroll != null ? config.scroll : ''}">
                            </div>
                            <div class="form-group">
                                <label class="setting-key">自动聚焦</label>
                                <label class="setting-key"><input type="radio" name="autofocusing" value="true" ${config.autoFocusing === true ? 'checked' : ''}> 是</label>
                                <label class="setting-key"><input type="radio" name="autofocusing" value="false" ${config.autoFocusing !== true ? 'checked' : ''}> 否</label>
                            </div>
                            <div class="form-group">
                                <label class="setting-key">检测@</label>
                                <label class="setting-key"><input type="radio" name="atdetect" value="true" ${config.atDetect === true ? 'checked' : ''}> 是</label>
                                <label class="setting-key"><input type="radio" name="atdetect" value="false" ${config.atDetect !== true ? 'checked' : ''}> 否</label>
                            </div>
                            <div class="form-group">
                                <label class="setting-key">Tab次数</label>
                                <input type="number" id="tab_times" min="7" max="8" value="${config.tabTimes != null ? config.tabTimes : ''}">
                            </div>
                        </details>

                        <button class="btn-primary" style="margin-top:20px;" onclick="saveSettings()">💾 保存设置</button>
                        <div id="settingsMessage"></div>

                        <hr style="margin: 30px 0; border: none; border-top: 1px solid var(--border-color);">

                        <h3 style="color: var(--danger-color); margin-bottom: 15px;">危险操作</h3>
                        <button class="btn-danger" onclick="showClearDatabaseConfirm()">删除所有聊天记录</button>
                    </div>
                `;
                content.innerHTML = settingsHtml;
            } catch (error) {
                content.innerHTML = `<div class="error-message">加载设置失败: ${error.message}</div>`;
            }
        }

        async function saveSettings() {
            const messageDiv = document.getElementById('settingsMessage');
            messageDiv.innerHTML = '';

            try {
                const settingsData = {
                    version: document.getElementById('version').value,
                    width: document.getElementById('width').value,
                    height: document.getElementById('height').value,
                    modelname: document.getElementById('modelname').value,
                    isvisionmodel: document.querySelector('input[name="isvisionmodel"]:checked').value,
                    api_key: document.getElementById('api_key').value,
                    server_url: document.getElementById('server_url').value,
                    scroll: document.getElementById('scroll').value,
                    autofocusing: document.querySelector('input[name="autofocusing"]:checked').value,
                    atdetect: document.querySelector('input[name="atdetect"]:checked').value,
                    tab_times: document.getElementById('tab_times').value,
                    remote_server_timeout: document.getElementById('remote_server_timeout').value,
                    maximagecount: document.getElementById('maximagecount').value,
                    SystemContent: document.getElementById('SystemContent').value,

                    // 新增
                    embedding_server_url: document.getElementById('embedding_server_url').value,
                    embedding_api_key: document.getElementById('embedding_api_key').value,
                    embedding_model_name: document.getElementById('embedding_model_name').value,
                    mapreduce_chunksize: document.getElementById('mapreduce_chunksize').value,
                    mapreduce_charbased: document.querySelector('input[name="mapreduce_charbased"]:checked').value
                };

                const response = await fetch('/api/config', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(settingsData)
                });
                const result = await response.json();

                if (result.success) {
                    messageDiv.innerHTML = '<div class="success-message">✅ 设置保存成功！</div>';
                    setTimeout(() => { messageDiv.innerHTML = ''; }, 2500);
                } else {
                    messageDiv.innerHTML = `<div class="error-message">保存失败: ${result.message}</div>`;
                }
            } catch (error) {
                messageDiv.innerHTML = `<div class="error-message">保存失败: ${error.message}</div>`;
            }
        }

        function escapeHtml(str) {
            const div = document.createElement('div');
            div.textContent = str;
            return div.innerHTML;
        }

        function showClearDatabaseConfirm() {
            document.getElementById('deleteConfirmModal').style.display = 'flex';
        }

        function closeDeleteConfirm() {
            document.getElementById('deleteConfirmModal').style.display = 'none';
        }

        async function clearDatabase() {
            closeDeleteConfirm();
            const content = document.getElementById('content');
            content.innerHTML = '<div class="loading">正在删除数据...</div>';

            try {
                const response = await fetch('/api/db/clear', { method: 'POST' });
                const result = await response.json();

                if (result.success) {
                    content.innerHTML = '<div class="success-message">删除成功！</div>';
                    setTimeout(showHome, 1500);
                } else {
                    content.innerHTML = `<div class="error-message">删除失败: ${result.message}</div>`;
                }
            } catch (error) {
                content.innerHTML = `<div class="error-message">删除失败: ${error.message}</div>`;
            }
        }

        document.addEventListener('DOMContentLoaded', showHome);
