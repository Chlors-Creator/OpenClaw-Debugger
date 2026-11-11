/*
 * 宿主桥接垫片（host shim）
 *
 * Windows 侧由 WebView2 原生提供 window.chrome.webview；
 * macOS 侧使用 Avalonia NativeWebView（WKWebView），只提供 invokeCSharpAction()。
 * 这里把前者映射到后者，使 bridge.js 无需任何平台分支。
 *
 * 必须在 bridge.js 之前加载，因为 bridge.js 在 create() 时会检测并注册监听。
 */
(function () {
  'use strict';

  // WebView2 环境：原生实现已存在，直接返回，行为不变。
  if (window.chrome && window.chrome.webview &&
      typeof window.chrome.webview.postMessage === 'function') return;

  var listeners = [];

  function postToHost(message) {
    var text = typeof message === 'string' ? message : JSON.stringify(message);
    if (typeof window.invokeCSharpAction === 'function') {
      window.invokeCSharpAction(text);
      return;
    }
    throw new Error('宿主桥接不可用：当前环境没有 invokeCSharpAction。');
  }

  window.chrome = window.chrome || {};
  window.chrome.webview = {
    addEventListener: function (type, handler) {
      if (type === 'message' && typeof handler === 'function') listeners.push(handler);
    },
    removeEventListener: function (type, handler) {
      if (type !== 'message') return;
      var index = listeners.indexOf(handler);
      if (index >= 0) listeners.splice(index, 1);
    },
    postMessage: postToHost
  };

  /* 宿主调用此函数把消息推回页面，参数可以是 JSON 字符串或已解析的对象。 */
  window.__openclawHostDeliver = function (payload) {
    var data = payload;
    if (typeof payload === 'string') {
      try { data = JSON.parse(payload); }
      catch (error) { console.error('宿主消息不是合法 JSON', error); return; }
    }
    listeners.slice().forEach(function (handler) {
      try { handler({ data: data }); }
      catch (error) { console.error('宿主消息处理失败', error); }
    });
  };
})();
