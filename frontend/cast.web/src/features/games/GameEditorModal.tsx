import { useState } from 'react';
import { api } from '@/lib/api';
import type { GameDetail } from '@/lib/types';
import { useTranslation } from 'react-i18next';
import { ImageCropperModal } from '@/components/ImageCropperModal';

interface Props {
    isOpen: boolean;
    onClose: () => void;
    gameToEdit?: GameDetail | null;
    onSuccess: () => void;
}

export function GameEditorModal({ isOpen, onClose, gameToEdit, onSuccess }: Props) {
    const { t } = useTranslation();
    const [loading, setLoading] = useState(false);
    const [interactionsJson, setInteractionsJson] = useState('');
    const [modManifestJson, setModManifestJson] = useState('');

    const [cropperSrc, setCropperSrc] = useState<string | null>(null);
    const [bannerBlob, setBannerBlob] = useState<Blob | null>(null);

    const handleJsonUpload = async (e: React.ChangeEvent<HTMLInputElement>, setter: (val: string) => void) => {
        const file = e.target.files?.[0];
        if (file) setter(await file.text());
    };

    const handleBannerSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        if (!file) return;
        const reader = new FileReader();
        reader.onload = () => setCropperSrc(reader.result as string);
        reader.readAsDataURL(file);
        e.target.value = '';
    };

    const onSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
        e.preventDefault();
        setLoading(true);
        try {
            const formData = new FormData(e.currentTarget);
            if (interactionsJson) formData.set('InteractionsJson', interactionsJson);
            if (modManifestJson) formData.set('ModManifestJson', modManifestJson);
            if (bannerBlob) formData.set('Banner', bannerBlob, 'banner.jpg');

            formData.set('IsEnabled', (e.currentTarget.elements.namedItem('IsEnabled') as HTMLInputElement).checked ? 'true' : 'false');

            if (gameToEdit) {
                await api.put(`/games/${gameToEdit.game.slug}`, formData);
            } else {
                await api.post('/games', formData);
            }
            onSuccess();
            onClose();
        } catch (err) {
            console.error(err);
            alert(t('common.error'));
        } finally {
            setLoading(false);
        }
    };

    if (!isOpen) return null;

    return (
        <>
            <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
                <div className="w-full max-w-2xl rounded-lg bg-bg-elevated p-6 shadow-xl">
                    <h2 className="mb-4 text-xl font-bold text-fg">
                        {gameToEdit ? t('games.editing') : t('games.creation')}
                    </h2>

                    <form onSubmit={onSubmit} className="space-y-4">
                        <div className="grid grid-cols-2 gap-4">
                            <div>
                                <label className="block text-sm text-fg-muted">{t('games.gameName')}</label>
                                <input name="Title" defaultValue={gameToEdit?.game.title ?? ''} required className="w-full rounded bg-bg-accent p-2 text-fg" />
                            </div>
                            <div>
                                <label className="block text-sm text-fg-muted">Slug</label>
                                <input name="Slug" defaultValue={gameToEdit?.game.slug ?? ''} required className="w-full rounded bg-bg-accent p-2 text-fg" />
                            </div>
                        </div>

                        <div className="grid grid-cols-2 gap-4">
                            <div>
                                <label className="block text-sm text-fg-muted">{t('games.genre')}</label>
                                <input name="Genre" defaultValue={gameToEdit?.game.genre ?? ''} className="w-full rounded bg-bg-accent p-2 text-fg" />
                            </div>
                            <div className="flex items-center pt-6">
                                <input type="checkbox" name="IsEnabled" defaultChecked={gameToEdit ? gameToEdit.game.isEnabled : true} className="mr-2" />
                                <label className="text-sm text-fg-muted">{t('games.gameState')}</label>
                            </div>
                        </div>

                        <div>
                            <label className="block text-sm text-fg-muted">Описание</label>
                            <textarea
                                name="Description"
                                defaultValue={gameToEdit?.game.description ?? ''}
                                rows={3}
                                className="w-full resize-none rounded bg-bg-accent p-2 text-sm text-fg"
                                placeholder="Краткое описание игры..."
                            />
                        </div>

                        <div>
                            <label className="block text-sm text-fg-muted">{t('games.banner')}</label>
                            <input type="file" accept="image/*" onChange={handleBannerSelect} className="w-full text-sm text-fg" />
                            {bannerBlob && <p className="mt-1 text-xs text-success">✓ Новое изображение готово к загрузке</p>}
                        </div>

                        <div className="grid grid-cols-2 gap-4 rounded border border-border p-3">
                            <div>
                                <label className="block text-sm text-fg-muted">{t('games.gameManifest')}</label>
                                <input type="file" accept=".json" onChange={(e) => handleJsonUpload(e, setInteractionsJson)} className="w-full text-sm text-fg" />
                                {interactionsJson && <span className="text-xs text-success">Загружен (в памяти)</span>}
                            </div>
                            <div>
                                <label className="block text-sm text-fg-muted">{t('games.gameFiles')}</label>
                                <input type="file" name="ModArchive" accept=".zip" className="w-full text-sm text-fg" />
                            </div>
                            <div className="col-span-2">
                                <label className="block text-sm text-fg-muted">{t('games.installationManifest')}</label>
                                <input type="file" accept=".json" onChange={(e) => handleJsonUpload(e, setModManifestJson)} className="w-full text-sm text-fg" />
                                {modManifestJson && <span className="text-xs text-success">Загружен (в памяти)</span>}
                            </div>
                        </div>

                        <div className="flex justify-end space-x-3 pt-4">
                            <button type="button" onClick={onClose} className="rounded px-4 py-2 text-fg-muted hover:bg-bg-accent">{t('common.cancel')}</button>
                            <button type="submit" disabled={loading} className="rounded bg-accent px-4 py-2 text-white">
                                {loading ? t('common.saving') : t('common.save')}
                            </button>
                        </div>
                    </form>
                </div>
            </div>

            {cropperSrc && (
                <ImageCropperModal
                    isOpen={!!cropperSrc}
                    imageSrc={cropperSrc}
                    aspectRatio={16 / 9}
                    onClose={() => setCropperSrc(null)}
                    onCropComplete={(blob) => {
                        setBannerBlob(blob);
                        setCropperSrc(null);
                    }}
                />
            )}
        </>
    );
}