document.addEventListener('DOMContentLoaded', () => {
    const apiSelect = document.getElementById('apiSelect');
    const textInputContainer = document.getElementById('textInputContainer');
    const jsonInputContainer = document.getElementById('jsonInputContainer');
    const textInputLabel = document.getElementById('textInputLabel');
    const questionInput = document.getElementById('questionInput');
    const jsonInput = document.getElementById('jsonInput');
    const analyzeSpContainer = document.getElementById('analyzeSpContainer');
    const spTextInput = document.getElementById('spTextInput');
    const usageLevelInput = document.getElementById('usageLevelInput');
    const submitBtn = document.getElementById('submitBtn');
    
    const loadingDiv = document.getElementById('loading');
    const resultsDiv = document.getElementById('results');
    const responseContent = document.getElementById('responseContent');
    const errorDiv = document.getElementById('error');

    // Handle dropdown changes to show/hide relevant inputs
    apiSelect.addEventListener('change', () => {
        const val = apiSelect.value;
        textInputContainer.classList.add('hidden');
        jsonInputContainer.classList.add('hidden');
        analyzeSpContainer.classList.add('hidden');

        if (val.startsWith('queue-')) {
            jsonInputContainer.classList.remove('hidden');
            
            // Helpful placeholder
            if (val === 'queue-sps') {
                jsonInput.placeholder = '{"storeProcedures": [...]}';
            } else if (val === 'queue-tables') {
                jsonInput.placeholder = '{"tables": [...]}';
            } else if (val === 'queue-relationships') {
                jsonInput.placeholder = '{"relationships": [...]}';
            } else if (val === 'queue-functions') {
                jsonInput.placeholder = '{"functions": [...]}';
            }
        } else if (val === 'analyze-sp') {
            analyzeSpContainer.classList.remove('hidden');
        } else {
            textInputContainer.classList.remove('hidden');
            jsonInputContainer.classList.add('hidden');
            
            if (val === 'generate-query') {
                textInputLabel.textContent = "Requirement:";
                questionInput.placeholder = "E.g., Give me all users with access to...";
            } else {
                textInputLabel.textContent = "Question:";
                questionInput.placeholder = "E.g., What does this do?";
            }
        }
    });

    submitBtn.addEventListener('click', performRequest);
    questionInput.addEventListener('keypress', (e) => {
        if (e.key === 'Enter') {
            performRequest();
        }
    });

    async function performRequest() {
        const endpoint = `/${apiSelect.value}`;
        let payload = {};

        if (apiSelect.value.startsWith('queue-')) {
            const rawJson = jsonInput.value.trim();
            if (!rawJson) {
                showError("Please enter a JSON payload.");
                return;
            }
            try {
                payload = JSON.parse(rawJson);
            } catch (e) {
                showError("Invalid JSON: " + e.message);
                return;
            }
        } else if (apiSelect.value === 'analyze-sp') {
            const spText = spTextInput.value.trim();
            if (!spText) {
                showError("Please enter the Stored Procedure text.");
                return;
            }
            const usageLevel = parseInt(usageLevelInput.value, 10) || 50;
            payload = { spText: spText, usageLevel: usageLevel };
        } else {
            const textVal = questionInput.value.trim();
            if (!textVal) {
                showError("Please enter text.");
                return;
            }
            
            if (apiSelect.value === 'generate-query') {
                payload = { requirement: textVal };
            } else {
                payload = { question: textVal };
            }
        }
        
        hideAll();
        loadingDiv.classList.remove('hidden');

        try {
            const response = await fetch(endpoint, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(payload)
            });

            const textData = await response.text();
            let data = null;
            try {
                data = textData ? JSON.parse(textData) : null;
            } catch (e) {
                data = textData; // fallback to raw string if it's not JSON
            }

            if (!response.ok) {
                let errorMsg = data?.message || "An error occurred.";
                if (data?.errors) {
                    errorMsg = Object.values(data.errors).flat().join('\n');
                } else if (typeof data === 'string') {
                    errorMsg = data;
                }
                throw new Error(`[${response.status}] ${errorMsg}`);
            }

            // Configure marked with highlight.js if available
            if (typeof marked !== 'undefined' && typeof hljs !== 'undefined') {
                marked.setOptions({
                    highlight: function(code, lang) {
                        const language = hljs.getLanguage(lang) ? lang : 'plaintext';
                        return hljs.highlight(code, { language }).value;
                    },
                    langPrefix: 'hljs language-'
                });
            }

            // Helper to preprocess single backtick `sql ...` to triple backtick ```sql ... ```
            function preprocessMarkdown(text) {
                if (!text) return text;
                return text.replace(/`sql\s+([^`]+)`/g, "```sql\n$1\n```");
            }

            if (data && data.analysis) {
                let markdownText = preprocessMarkdown(data.analysis);
                let html = typeof marked !== 'undefined' ? marked.parse(markdownText) : `<pre>${markdownText}</pre>`;
                
                if (data.extractedTables && data.extractedTables.length > 0) {
                    html += `<h3>Extracted Tables</h3><ul>` + data.extractedTables.map(t => `<li><code>${t}</code></li>`).join('') + `</ul>`;
                }
                
                if (data.extractedFilters && data.extractedFilters.length > 0) {
                    html += `<h3>Extracted Filters</h3><pre><code class="hljs language-sql">` + data.extractedFilters.join('\n') + `</code></pre>`;
                }
                
                responseContent.innerHTML = html;
            } else if (data && data.explanation) {
                let markdownText = preprocessMarkdown(data.explanation);
                let html = typeof marked !== 'undefined' ? marked.parse(markdownText) : `<pre>${markdownText}</pre>`;
                responseContent.innerHTML = html;
            } else {
                const text = typeof data === 'string' ? data : JSON.stringify(data, null, 2);
                responseContent.innerHTML = `<pre>${text}</pre>`;
            }

            hideAll();
            resultsDiv.classList.remove('hidden');

        } catch (err) {
            hideAll();
            showError(err.message);
        }
    }

    function hideAll() {
        loadingDiv.classList.add('hidden');
        resultsDiv.classList.add('hidden');
        errorDiv.classList.add('hidden');
    }

    function showError(message) {
        errorDiv.textContent = message;
        errorDiv.classList.remove('hidden');
    }
});
