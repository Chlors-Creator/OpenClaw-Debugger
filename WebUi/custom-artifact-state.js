(function () {
  'use strict';

  window.OpenClawCustomArtifactState = {
    create() {
      return { plugins: [], skills: [], loaded: false, loading: false, uploading: false, deletingId: '' };
    }
  };
})();
