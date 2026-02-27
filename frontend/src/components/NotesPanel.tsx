import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import DOMPurify from 'dompurify';
import { marked } from 'marked';
import { formatDistanceToNow } from 'date-fns';
import { notesApi } from '../api/client';
import type { Note } from '../types';

interface NotesPanelProps {
  serverId: string;
}

export function NotesPanel({ serverId }: NotesPanelProps) {
  const [newContent, setNewContent] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const queryClient = useQueryClient();

  const { data, isLoading } = useQuery({
    queryKey: ['notes', serverId],
    queryFn: () => notesApi.list(serverId),
  });

  const createMutation = useMutation({
    mutationFn: (content: string) => notesApi.create(serverId, { content }),
    onSuccess: () => {
      setNewContent('');
      queryClient.invalidateQueries({ queryKey: ['notes', serverId] });
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (noteId: string) => notesApi.delete(noteId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['notes', serverId] }),
  });

  const pinMutation = useMutation({
    mutationFn: ({ noteId, isPinned }: { noteId: string; isPinned: boolean }) =>
      notesApi.update(noteId, { isPinned }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['notes', serverId] }),
  });

  const handleSubmit = async () => {
    if (!newContent.trim()) return;
    setIsSubmitting(true);
    try {
      await createMutation.mutateAsync(newContent);
    } finally {
      setIsSubmitting(false);
    }
  };

  const renderMarkdown = (content: string): string => {
    const raw = marked.parse(content, { async: false }) as string;
    return DOMPurify.sanitize(raw);
  };

  const notes = data?.items ?? [];

  return (
    <div className="space-y-4">
      {/* Add note form */}
      <div className="bg-gray-50 rounded-lg p-4 border border-gray-200">
        <h3 className="text-sm font-medium text-gray-700 mb-2">Add Note</h3>
        <textarea
          value={newContent}
          onChange={(e) => setNewContent(e.target.value)}
          className="w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 text-sm font-mono"
          rows={4}
          placeholder="Write a note in Markdown..."
        />
        <div className="mt-2 flex justify-between items-center">
          <span className="text-xs text-gray-500">Markdown supported</span>
          <button
            onClick={handleSubmit}
            disabled={isSubmitting || !newContent.trim()}
            className="px-3 py-1.5 text-sm font-medium text-white bg-blue-600 rounded-md hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {isSubmitting ? 'Saving...' : 'Add Note'}
          </button>
        </div>
      </div>

      {/* Notes list */}
      {isLoading ? (
        <div className="text-center py-8 text-gray-500">Loading notes...</div>
      ) : notes.length === 0 ? (
        <div className="text-center py-8 text-gray-400">No notes yet. Add one above.</div>
      ) : (
        <div className="space-y-3">
          {notes.map((note: Note) => (
            <NoteCard
              key={note.id}
              note={note}
              renderMarkdown={renderMarkdown}
              onPin={() => pinMutation.mutate({ noteId: note.id, isPinned: !note.isPinned })}
              onDelete={() => {
                if (confirm('Delete this note?')) deleteMutation.mutate(note.id);
              }}
            />
          ))}
        </div>
      )}
    </div>
  );
}

function NoteCard({
  note,
  renderMarkdown,
  onPin,
  onDelete,
}: {
  note: Note;
  renderMarkdown: (content: string) => string;
  onPin: () => void;
  onDelete: () => void;
}) {
  return (
    <div className={`rounded-lg border p-4 ${note.isPinned ? 'border-yellow-300 bg-yellow-50' : 'border-gray-200 bg-white'}`}>
      <div className="flex items-start justify-between gap-2 mb-2">
        <div className="flex items-center gap-2 text-xs text-gray-500">
          <span className="font-medium text-gray-700">{note.createdBy}</span>
          <span>·</span>
          <span>{formatDistanceToNow(new Date(note.createdAt), { addSuffix: true })}</span>
          {note.updatedAt && (
            <>
              <span>·</span>
              <span className="italic">edited</span>
            </>
          )}
          {note.isPinned && (
            <span className="inline-flex items-center px-1.5 py-0.5 rounded text-xs bg-yellow-100 text-yellow-800">
              📌 Pinned
            </span>
          )}
        </div>
        <div className="flex items-center gap-1">
          <button
            onClick={onPin}
            className="p-1 text-gray-400 hover:text-yellow-600 rounded"
            title={note.isPinned ? 'Unpin' : 'Pin to top'}
          >
            {note.isPinned ? '📌' : '📍'}
          </button>
          <button
            onClick={onDelete}
            className="p-1 text-gray-400 hover:text-red-600 rounded"
            title="Delete note"
          >
            🗑
          </button>
        </div>
      </div>
      <div
        className="prose prose-sm max-w-none text-gray-700"
        dangerouslySetInnerHTML={{ __html: renderMarkdown(note.content) }}
      />
    </div>
  );
}
