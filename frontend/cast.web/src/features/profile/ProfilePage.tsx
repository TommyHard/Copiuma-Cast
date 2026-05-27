import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api } from '@/lib/api';
import type { Profile, UserStatus } from '@/lib/types';
import { SUPPORTED_LANGUAGES } from '@/i18n';
import { ImageCropperModal } from '@/components/ImageCropperModal';
import { StreamerApplicationSection } from './StreamerApplicationSection';

const STATUSES: UserStatus[] = ['Online', 'Away', 'DoNotDisturb', 'Offline'];

export function ProfilePage() {
    const { t, i18n } = useTranslation();
    const qc = useQueryClient();
    const fileRef = useRef<HTMLInputElement>(null);

    const { data, isLoading } = useQuery({
        queryKey: ['profile'],
        queryFn: async () => (await api.get<Profile>('/profile/me')).data,
    });

    const [displayName, setDisplayName] = useState('');
    const [handle, setHandle] = useState('');
    const [language, setLanguage] = useState('en');
    const [saved, setSaved] = useState(false);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        if (data) { setDisplayName(data.displayName); setHandle(data.handle); setLanguage(data.language); }
    }, [data]);

    async function refresh() { await qc.invalidateQueries({ queryKey: ['profile'] }); }

    async function saveProfile(e: FormEvent) {
        e.preventDefault();
        setError(null); setSaved(false);
        try {
            await api.put('/profile', { displayName, handle, language });
            i18n.changeLanguage(language);
            setSaved(true);
            await refresh();
        } catch (err: any) {
            setError(err?.response?.status === 409 ? t('auth.handleTaken') : t('common.error'));
        }
    }

    async function setStatus(status: UserStatus) {
        await api.put('/profile/status', { status });
        await refresh();
    }

    const [cropperOpen, setCropperOpen] = useState(false);
    const [imageSrc, setImageSrc] = useState('');

    const onFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        if (!file) return;
        const reader = new FileReader();
        reader.onload = () => {
            setImageSrc(reader.result as string);
            setCropperOpen(true);
        };
        reader.readAsDataURL(file);
        e.target.value = '';
    };

    const handleCroppedAvatar = async (blob: Blob) => {
        const form = new FormData();
        form.append('file', blob, 'avatar.jpg');
        try {
            await api.post('/profile/avatar', form);
            await refresh();
        } catch (error) {
            console.error(error);
        } finally {
            setCropperOpen(false);
        }
    };

    if (isLoading || !data) return <p className="text-fg-muted">{t('common.loading')}</p>;

    const input = 'w-full rounded-md border border-border bg-bg px-3 py-2 text-fg outline-none focus:border-accent';

    return (
        <div className="mx-auto max-w-xl space-y-6">
            <h1 className="text-2xl font-bold text-fg">{t('profile.title')}</h1>

            <div className="flex items-center gap-4">
                <div className="h-16 w-16 overflow-hidden rounded-full bg-bg-accent">
                    {data.avatarUrl && <img src={data.avatarUrl} alt="" className="h-full w-full object-cover" />}
                </div>
                <button type="button" onClick={() => fileRef.current?.click()}
                    className="rounded-md border border-border px-3 py-1.5 text-sm text-fg hover:border-accent">
                    {t('profile.uploadAvatar')}
                </button>
                <input ref={fileRef} type="file" accept="image/*" hidden onChange={onFileChange} />
            </div>

            <form onSubmit={saveProfile} className="space-y-3">
                <label className="block text-sm text-fg-muted">{t('profile.displayName')}
                    <input className={input} value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
                </label>
                <label className="block text-sm text-fg-muted">{t('profile.handle')}
                    <input className={input} value={handle} onChange={(e) => setHandle(e.target.value)} />
                </label>
                <label className="block text-sm text-fg-muted">{t('profile.language')}
                    <select className={input} value={language} onChange={(e) => setLanguage(e.target.value)}>
                        {SUPPORTED_LANGUAGES.map((l) => <option key={l} value={l}>{l.toUpperCase()}</option>)}
                    </select>
                </label>
                {error && <p className="text-sm text-danger">{error}</p>}
                {saved && <p className="text-sm text-success">{t('profile.saved')}</p>}
                <button type="submit" className="rounded-md bg-accent px-4 py-2 font-medium text-accent-fg hover:opacity-90">
                    {t('common.save')}
                </button>
            </form>

            <section>
                <h2 className="mb-2 text-sm font-semibold text-fg">{t('profile.status')}</h2>
                <div className="flex flex-wrap gap-2">
                    {STATUSES.map((s) => (
                        <button key={s} type="button" onClick={() => void setStatus(s)}
                            className={`rounded-md border px-3 py-1.5 text-sm ${data.status === s ? 'border-accent text-accent' : 'border-border text-fg hover:border-accent'}`}>
                            {t(`status.${s}`)}
                        </button>
                    ))}
                </div>
            </section>

            <StreamerApplicationSection />

            <ImageCropperModal
                isOpen={cropperOpen}
                imageSrc={imageSrc}
                aspectRatio={1}
                onClose={() => setCropperOpen(false)}
                onCropComplete={handleCroppedAvatar}
            />
        </div>
    );
}