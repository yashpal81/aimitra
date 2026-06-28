(function () {
  // 1. Get configuration from the script tag
  const scriptTag = document.getElementById('ai-chatbot-loader');
  const chatUrl = scriptTag.getAttribute('data-app-url');
  const sessionCollection = scriptTag.getAttribute('data-session-collection') || '';

  // 2. Create iframe URL with sessionId query parameter
  const iframeUrl = buildIframeUrl(chatUrl, sessionCollection);

  function buildIframeUrl(baseUrl, sessionId) {
    if (!baseUrl) {
      console.error('ai-chatbot-loader: data-app-url is missing');
      return baseUrl;
    }

    const separator = baseUrl.includes('?') ? '&' : '?';
    return baseUrl + separator + 'sessionId=' + encodeURIComponent(sessionId);
  }

  // 3. Create and inject CSS directly into the host page
  const style = document.createElement('style');
  style.innerHTML = `
    .ai-chat-widget-container { position: fixed; bottom: 20px; right: 20px; z-index: 999999; font-family: sans-serif; }
    .ai-chat-button { width: 60px; height: 60px; border-radius: 50%; background-color: #007bff; color: white; display: flex; align-items: center; justify-content: center; cursor: pointer; box-shadow: 0 4px 12px rgba(0,0,0,0.15); transition: transform 0.2s; }
    .ai-chat-button:hover { transform: scale(1.05); }
    .ai-chat-iframe-wrapper { display: none; position: fixed; bottom: 90px; right: 20px; width: 400px; height: 600px; border-radius: 12px; box-shadow: 0 5px 40px rgba(0,0,0,0.16); overflow: hidden; border: 1px solid #e0e0e0; background: #fff; }
    .ai-chat-iframe-wrapper.open { display: block; }
    @media (max-width: 450px) {
      .ai-chat-iframe-wrapper { width: 100%; height: 100%; bottom: 0; right: 0; border-radius: 0; }
    }
  `;
  document.head.appendChild(style);

  // 4. Create the Widget HTML Structure with sessionId in iframe URL
  const container = document.createElement('div');
  container.className = 'ai-chat-widget-container';

  const iframe = document.createElement('iframe');
  iframe.src = iframeUrl;
  iframe.style.cssText = 'width:100%; height:100%; border:none;';
  iframe.setAttribute('allow', 'microphone');

  const iframeWrapper = document.createElement('div');
  iframeWrapper.className = 'ai-chat-iframe-wrapper';
  iframeWrapper.id = 'aiChatWrapper';
  iframeWrapper.appendChild(iframe);

  const chatButton = document.createElement('div');
  chatButton.className = 'ai-chat-button';
  chatButton.id = 'aiChatBtn';
  chatButton.innerHTML = `
    <!-- Chat Icon (SVG) -->
    <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
      <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"></path>
    </svg>
  `;

  container.appendChild(iframeWrapper);
  container.appendChild(chatButton);
  document.body.appendChild(container);

  // 5. Toggle Visibility Logic
  const chatBtn = document.getElementById('aiChatBtn');
  const chatWrapper = document.getElementById('aiChatWrapper');

  chatBtn.addEventListener('click', () => {
    chatWrapper.classList.toggle('open');
  });

  // 6. Log initialization for debugging
  console.log('AI Chat Widget initialized with sessionId:', sessionCollection ? sessionCollection.substring(0, 8) + '...' : '(none)');
})();