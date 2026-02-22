import React, { useState, useEffect, useRef } from 'react';
import './App.css';

// ============ YOUR CREDENTIALS (replace these) ============
const PLAYFAB_TITLE_ID = '18685E';
const FB_APP_ID = '26474593118832262';
const METAPERSON_CLIENT_ID = 'qsKhwqFxWwq0NVHFbfAJryqTyWO9kXfJSUVb5jYA';     // <-- replace
const METAPERSON_CLIENT_SECRET = 'mGIFSEwJnd1TV45EqPeJ1YyiNaRmu30nzMrDVsicohN7uUMWWLTaxXUxCdK2tHRWEdoea3GtVjiCQt72mKNR2NBDmJrmHmAiSjC8mjdBvIKiuYaFmi6b7KlYbJwV5DZO'; // <-- replace
// ===========================================================

function App() {
  const [user, setUser] = useState(null);
  const [playFabId, setPlayFabId] = useState(null);
  const [sessionTicket, setSessionTicket] = useState(null);
  const [avatarUrl, setAvatarUrl] = useState(null);
  const [step, setStep] = useState('login'); // login | create | done
  const iframeRef = useRef(null);

  // Load Facebook SDK
  useEffect(() => {
    window.fbAsyncInit = function () {
      window.FB.init({
        appId: FB_APP_ID,
        cookie: true,
        xfbml: true,
        version: 'v19.0',
      });
    };
    // Load FB SDK script
    (function (d, s, id) {
      var js, fjs = d.getElementsByTagName(s)[0];
      if (d.getElementById(id)) return;
      js = d.createElement(s); js.id = id;
      js.src = 'https://connect.facebook.net/en_US/sdk.js';
      fjs.parentNode.insertBefore(js, fjs);
    })(document, 'script', 'facebook-jssdk');
  }, []);

  // Listen for MetaPerson avatar export
  useEffect(() => {
    const handleMessage = (event) => {
      if (event.data?.eventName === 'metaperson_creator_loaded') {
        // Authenticate MetaPerson iframe
        const iframe = iframeRef.current;
        if (iframe) {
          iframe.contentWindow.postMessage(
            {
              eventName: 'authenticate',
              clientId: METAPERSON_CLIENT_ID,
              clientSecret: METAPERSON_CLIENT_SECRET,
            },
            '*'
          );
        }
      }

      if (event.data?.eventName === 'avatar_exported') {
        const url = event.data.data.url;
        console.log('Avatar GLB URL:', url);
        setAvatarUrl(url);
        saveAvatarToPlayFab(url);
        setStep('done');
      }
    };

    window.addEventListener('message', handleMessage);
    return () => window.removeEventListener('message', handleMessage);
  }, [sessionTicket]);

  // Facebook Login → PlayFab
  const handleFacebookLogin = () => {
    window.FB.login(
      (response) => {
        if (response.authResponse) {
          const accessToken = response.authResponse.accessToken;
          setUser({ name: 'Facebook User', token: accessToken });

          // Login to PlayFab with Facebook token
          fetch(`https://${PLAYFAB_TITLE_ID}.playfabapi.com/Client/LoginWithFacebook`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
              TitleId: PLAYFAB_TITLE_ID,
              AccessToken: accessToken,
              CreateAccount: true,
            }),
          })
            .then((res) => res.json())
            .then((data) => {
              if (data.data) {
                console.log('PlayFab login success:', data.data.PlayFabId);
                setPlayFabId(data.data.PlayFabId);
                setSessionTicket(data.data.SessionTicket);
                
                // Check if user already has an avatar
                loadExistingAvatar(data.data.SessionTicket);
                setStep('create');
              } else {
                console.error('PlayFab login failed:', data);
                alert('PlayFab login failed. Check console.');
              }
            })
            .catch((err) => console.error('PlayFab error:', err));
        }
      },
      { scope: 'public_profile,email' }
    );
  };

  // Save avatar URL to PlayFab
  const saveAvatarToPlayFab = (url) => {
    if (!sessionTicket) return;
    fetch(`https://${PLAYFAB_TITLE_ID}.playfabapi.com/Client/UpdateUserData`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Authorization': sessionTicket,
      },
      body: JSON.stringify({
        Data: {
          avatarUrl: url,
          avatarSource: 'metaperson',
          createdAt: new Date().toISOString(),
        },
      }),
    })
      .then((res) => res.json())
      .then((data) => console.log('Avatar saved to PlayFab!', data))
      .catch((err) => console.error('Save error:', err));
  };

  // Load existing avatar from PlayFab
  const loadExistingAvatar = (ticket) => {
    fetch(`https://${PLAYFAB_TITLE_ID}.playfabapi.com/Client/GetUserData`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Authorization': ticket,
      },
      body: JSON.stringify({ Keys: ['avatarUrl'] }),
    })
      .then((res) => res.json())
      .then((data) => {
        if (data.data?.Data?.avatarUrl) {
          const url = data.data.Data.avatarUrl.Value;
          console.log('Existing avatar found:', url);
          setAvatarUrl(url);
        }
      })
      .catch((err) => console.error('Load error:', err));
  };

  // Logout
  const handleLogout = () => {
    window.FB.logout();
    setUser(null);
    setPlayFabId(null);
    setSessionTicket(null);
    setAvatarUrl(null);
    setStep('login');
  };

  return (
    <div className="App">
      <header className="App-header">
        <h1>🎮 DahVarsity Avatar Creator</h1>

        {step === 'login' && (
          <div>
            <p>Login with Facebook to create your avatar</p>
            <button onClick={handleFacebookLogin} style={styles.fbButton}>
              🔵 Login with Facebook
            </button>
          </div>
        )}

        {step === 'create' && (
          <div style={{ width: '100%' }}>
            <p>Welcome! PlayFab ID: {playFabId}</p>
            {avatarUrl && (
              <div style={styles.existingAvatar}>
                <p>✅ You already have an avatar!</p>
                <p style={{ fontSize: '12px', wordBreak: 'break-all' }}>{avatarUrl}</p>
                <button onClick={() => navigator.clipboard.writeText(avatarUrl)} style={styles.copyButton}>
                  📋 Copy Avatar URL
                </button>
              </div>
            )}
            <p>Create {avatarUrl ? 'a new' : 'your'} avatar below:</p>
            <iframe
              ref={iframeRef}
              src="https://metaperson.avatarsdk.com/iframe.html"
              allow="fullscreen microphone camera"
              style={{ width: '100%', height: '600px', border: 'none', borderRadius: '8px' }}
              title="MetaPerson Creator"
            />
            <button onClick={handleLogout} style={styles.logoutButton}>
              Logout
            </button>
          </div>
        )}

        {step === 'done' && (
          <div>
            <h2>✅ Avatar Created & Saved!</h2>
            <p style={{ fontSize: '12px', wordBreak: 'break-all' }}>{avatarUrl}</p>
            <button onClick={() => navigator.clipboard.writeText(avatarUrl)} style={styles.copyButton}>
              📋 Copy Avatar URL (use in Unity)
            </button>
            <br />
            <button onClick={() => setStep('create')} style={styles.copyButton}>
              Create Another Avatar
            </button>
            <br />
            <button onClick={handleLogout} style={styles.logoutButton}>
              Logout
            </button>
          </div>
        )}
      </header>
    </div>
  );
}

const styles = {
  fbButton: {
    backgroundColor: '#1877F2', color: 'white', border: 'none',
    padding: '15px 30px', fontSize: '18px', borderRadius: '8px',
    cursor: 'pointer', marginTop: '20px',
  },
  copyButton: {
    backgroundColor: '#4CAF50', color: 'white', border: 'none',
    padding: '10px 20px', fontSize: '14px', borderRadius: '6px',
    cursor: 'pointer', margin: '10px',
  },
  logoutButton: {
    backgroundColor: '#666', color: 'white', border: 'none',
    padding: '10px 20px', fontSize: '14px', borderRadius: '6px',
    cursor: 'pointer', margin: '10px',
  },
  existingAvatar: {
    backgroundColor: '#1a3a1a', padding: '15px', borderRadius: '8px',
    margin: '10px 0',
  },
};

export default App;