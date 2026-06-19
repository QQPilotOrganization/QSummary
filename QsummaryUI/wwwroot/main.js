        let currentGroupName = '';

        async function showHome() {
            const content = document.getElementById('content');
            content.innerHTML = '<div class="loading">加载中...</div>';

            try {
                // 添加时间戳参数防止缓存
                const response = await fetch(`/api/groups?t=${Date.now()}`);
                const groups = await response.json();

                if (groups.length === 0) {
                    content.innerHTML = '<div class="empty-state">暂无群组数据</div>';
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
                            <div class="card-actions">
                                <button class="btn-primary" onclick="showConfirmDialog('${escapeHtml(group)}')">开始总结</button>
                                <button class="btn-secondary" onclick="showComments('${escapeHtml(group)}')">查看聊天记录</button>
                            </div>
                        </div>
                    `;
                });
                content.innerHTML = html;
            } catch (error) {
                content.innerHTML = `<div class="error-message">加载失败: ${error.message}</div>`;
            }
        }

        function showConfirmDialog(groupName) {
            currentGroupName = groupName;
            document.getElementById('confirmMessage').textContent = `确定要对「${currentGroupName}」进行聊天记录总结吗？`;
            document.getElementById('confirmModal').style.display = 'flex';
        }

        function closeModal() {
            document.getElementById('confirmModal').style.display = 'none';
        }

        function confirmSummary() {
            closeModal();
            confirmSummaryNext(currentGroupName)

        }
        async function confirmSummaryNext(gname) {
            const content = document.getElementById('content');
            content.innerHTML = '<div class="loading">正在总结中...</div>';

            try {
                console.log(JSON.stringify({ groupName: gname }))
                const response = await fetch('/api/summary', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ groupName: gname })
                });

                const result = await response.json();

                if (result.success) {
                    showSummaryResult(gname, result.summary);
                } else {
                    content.innerHTML = `<div class="error-message">总结失败: ${result.message}</div>`;
                }
            } catch (error) {
                content.innerHTML = `<div class="error-message">总结失败: ${error.message}</div>`;
            }
        }

        function showSummaryResult(groupName, summary) {
            const content = document.getElementById('content');
            content.innerHTML = `
                <div class="summary-container">
                    <h2>${escapeHtml(groupName)} - 总结结果</h2>
                    <textarea id="summary" class="summary-text" readonly>${escapeHtml(summary)}</textarea>
                    <button class="btn-secondary" style="margin-top: 15px;" onclick="showHome()">返回主页</button>
                </div>
            `;
        }

        async function showComments(groupName) {
            const content = document.getElementById('content');
            content.innerHTML = '<div class="loading">加载聊天记录中...</div>';

            try {
                // 添加时间戳参数防止缓存
                const response = await fetch(`/api/comments/${encodeURIComponent(groupName)}?t=${Date.now()}`);
                const comments = await response.json();
                console.log(comments);

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
                        <h2>${escapeHtml(groupName)} - 聊天记录</h2>
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

        async function showSettings() {
            const content = document.getElementById('content');
            content.innerHTML = '<div class="loading">加载设置中...</div>';

            try {
                const response = await fetch('/api/config');
                const config = await response.json();

                const settingsHtml = `
                    <div class="settings-form" id="settingsForm">
                        <h2>系统设置</h2>
                        
                        <div class="form-group">
                            <label class="setting-key">版本</label class="setting-key">
                            <input type="text" id="version" value="${escapeHtml(config.version || '')}" readonly>
                        </div>
                        <div class="form-group">
                            <label class="setting-key">宽度</label class="setting-key">
                            <input type="number" id="width" value="${config.width != null ? config.width : ''}">
                        </div>
                        <div class="form-group">
                            <label class="setting-key">高度</label class="setting-key">
                            <input type="number" id="height" value="${config.height != null ? config.height : ''}">
                        </div>
                        <div class="form-group">
                            <label class="setting-key">模型名称</label class="setting-key">
                            <input type="text" id="modelname" value="${escapeHtml(config.modelName || '')}">
                        </div>
                        <div class="form-group">
                            <label class="setting-key">视觉模型</label class="setting-key">
                            <label class="setting-key"><input type="radio" name="isvisionmodel" value="true" ${config.isVisionModel === true ? 'checked' : ''}> 是</label class="setting-key">
                            <label class="setting-key"><input type="radio" name="isvisionmodel" value="false" ${config.isVisionModel !== true ? 'checked' : ''}> 否</label class="setting-key">
                        </div>
                        <div class="form-group">
                            <label class="setting-key">API Key</label class="setting-key">
                            <input type="text" id="api_key" value="${escapeHtml(config.apiKey || '')}">
                        </div>
                        <div class="form-group">
                            <label class="setting-key">服务器地址</label class="setting-key">
                            <input type="text" id="server_url" value="${escapeHtml(config.serverUrl || '')}">
                        </div>
                        <div class="form-group">
                            <label class="setting-key">滚动次数</label class="setting-key">
                            <input type="number" id="scroll" value="${config.scroll != null ? config.scroll : ''}">
                        </div>
                        <div class="form-group">
                            <label class="setting-key">自动聚焦</label class="setting-key">
                            <label class="setting-key"><input type="radio" name="autofocusing" value="true" ${config.autoFocusing === true ? 'checked' : ''}> 是</label class="setting-key">
                            <label class="setting-key"><input type="radio" name="autofocusing" value="false" ${config.autoFocusing !== true ? 'checked' : ''}> 否</label class="setting-key">
                        </div>
                        <div class="form-group">
                            <label class="setting-key">检测@</label class="setting-key">
                            <label class="setting-key"><input type="radio" name="atdetect" value="true" ${config.atDetect === true ? 'checked' : ''}> 是</label class="setting-key">
                            <label class="setting-key"><input type="radio" name="atdetect" value="false" ${config.atDetect !== true ? 'checked' : ''}> 否</label class="setting-key">
                        </div>
                        <div class="form-group">
                            <label class="setting-key">Tab次数</label class="setting-key">
                            <input type="number" id="tab_times" min="7" max="8" value="${config.tabTimes != null ? config.tabTimes : ''}">
                        </div>
                        <div class="form-group">
                            <label class="setting-key">超时时间</label class="setting-key">
                            <input type="number" id="remote_server_timeout" value="${config.remoteServerTimeout != null ? config.remoteServerTimeout : ''}">
                        </div>
                        <div class="form-group">
                            <label class="setting-key">最大图片数量</label class="setting-key">
                            <input type="number" min="1" id="maximagecount" value="${config.maxImageCount != null ? config.maxImageCount : '1'}">
                        </div>
                        <!-- 缩放比例已隐藏 -->
                        <!-- <div class="form-group">
                            <label class="setting-key" >缩放比例</label class="setting-key">
                            <input type="number" step="0.01" id="scale" value="${config.scale != null ? config.scale : ''}" >
                        </div> -->
                        <div class="form-group">
                            <label class="setting-key">System Prompt</label class="setting-key">
                            <textarea id="SystemContent">${escapeHtml(config.systemContent || '')}</textarea>
                        </div>

                        <button class="btn-primary" onclick="saveSettings()">保存设置</button>
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
                    // scale 已隐藏，不再保存
                    SystemContent: document.getElementById('SystemContent').value
                };

                const response = await fetch('/api/config', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(settingsData)
                });
                const result = await response.json();

                if (result.success) {
                    messageDiv.innerHTML = '<div class="success-message">设置保存成功！</div>';
                    setTimeout(() => { messageDiv.innerHTML = ''; }, 2000);
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
                const response = await fetch('/api/db/clear', {
                    method: 'POST'
                });
                const result = await response.json();

                if (result.success) {
                    content.innerHTML = '<div class="success-message">删除成功！</div>';
                    setTimeout(() => {
                        showHome();
                    }, 1500);
                } else {
                    content.innerHTML = `<div class="error-message">删除失败: ${result.message}</div>`;
                }
            } catch (error) {
                content.innerHTML = `<div class="error-message">删除失败: ${error.message}</div>`;
            }
        }

        // 页面加载时显示主页
        document.addEventListener('DOMContentLoaded', showHome);