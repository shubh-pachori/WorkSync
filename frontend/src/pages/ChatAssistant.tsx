import { useState } from 'react';
import { ChatApi } from '../api/timesheetApi';
import { describeApiError } from '../api/client';
import { useCurrentUser } from '../auth/AuthContext';

interface Message { role: 'user' | 'ai'; text: string; }

/** AI chat over the caller's own activity log. */
export default function ChatAssistant() {
  const user = useCurrentUser();

  const [messages, setMessages] = useState<Message[]>([{
    role: 'ai',
    text: `Hi ${user.fullName.split(' ')[0]}, ask me anything about your logged work — for example "What did I work on last Thursday?"`
  }]);
  const [input, setInput] = useState('');
  const [loading, setLoading] = useState(false);

  const send = async () => {
    const question = input.trim();
    if (!question || loading) return;

    setMessages(m => [...m, { role: 'user', text: question }]);
    setInput('');
    setLoading(true);

    try {
      const res = await ChatApi.ask(question);
      setMessages(m => [...m, { role: 'ai', text: res.answer }]);
    } catch (err) {
      setMessages(m => [...m, { role: 'ai', text: describeApiError(err, 'Sorry, I could not reach the AI service.') }]);
    } finally {
      setLoading(false);
    }
  };

  return (
    <>
      <div className="page-header">
        <span className="eyebrow">AI Chat</span>
        <h1>Ask about your work history</h1>
        <p>Grounded in your connected commits, tickets and meetings.</p>
      </div>

      <div className="card">
        <div className="chat-window">
          {messages.map((m, i) => (
            <div key={i} className={`chat-bubble ${m.role === 'user' ? 'chat-user' : 'chat-ai'}`}>{m.text}</div>
          ))}
          {loading && <div className="chat-bubble chat-ai">Thinking…</div>}
        </div>

        <div className="chat-input-row">
          <input
            value={input}
            onChange={e => setInput(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') void send(); }}
            placeholder="What did I work on last Thursday?"
            aria-label="Ask a question about your work"
          />
          <button className="btn btn-accent" onClick={() => void send()} disabled={loading || !input.trim()}>
            Send
          </button>
        </div>
      </div>
    </>
  );
}
