document.addEventListener('DOMContentLoaded', () => {
    const apiSelect = document.getElementById('apiSelect');
    const textInputContainer = document.getElementById('textInputContainer');
    const jsonInputContainer = document.getElementById('jsonInputContainer');
    const textInputLabel = document.getElementById('textInputLabel');
    const questionInput = document.getElementById('questionInput');
    const jsonInput = document.getElementById('jsonInput');
    const submitBtn = document.getElementById('submitBtn');
    
    const loadingDiv = document.getElementById('loading');
    const resultsDiv = document.getElementById('results');
    const responseJsonPre = document.getElementById('responseJson');
    const errorDiv = document.getElementById('error');

    // Handle dropdown changes to show/hide relevant inputs
    apiSelect.addEventListener('change', () => {
        const val = apiSelect.value;
        if (val.startsWith('queue-')) {
            textInputContainer.classList.add('hidden');
            jsonInputContainer.classList.remove('hidden');
            
            // Helpful placeholder
            if (val === 'queue-sps') {
                jsonInput.placeholder = '{"storeProcedures": [...]}';
            } else if (val === 'queue-tables') {
                jsonInput.placeholder = '{"tables": [...]}';
            } else if (val === 'queue-functions') {
                jsonInput.placeholder = '{"functions": [...]}';
            }
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

            responseJsonPre.textContent = typeof data === 'string' ? data : JSON.stringify(data, null, 2);

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
